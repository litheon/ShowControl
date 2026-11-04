using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using System;
using System.Runtime.CompilerServices;
using Buffer = Silk.NET.Vulkan.Buffer;
using Device = Silk.NET.Vulkan.Device;

namespace ShowControl.Graphics
{
    /// <summary>
    /// The compute prelude for the "fluid" shader: owns every GPU resource of the fluid
    /// simulation — the two device-local ping-pong state buffers, the compute pipeline, and
    /// the recording of the per-frame dispatch with its barriers. The state buffers are the
    /// effect's entire memory; nothing on the CPU tracks simulation history.
    ///
    /// The host's involvement is deliberately generic: it creates this pass at startup when
    /// a shader ships a compute stage, lets it write its state bindings into the shared
    /// descriptor sets, and calls <see cref="Record"/> before the render pass while the
    /// owning shader is active. No fluid-specific code lives in <c>GraphicsHost</c>.
    /// </summary>
    internal sealed unsafe class FluidSimulationPass
    {
        /// <summary>Grid size — must match fluid.comp and fluid.frag (three places that
        /// must agree; the shaders hard-code these as GRID_W/GRID_H).</summary>
        internal const int GridWidth = 288;
        internal const int GridHeight = 256;

        /// <summary>32 bytes per cell: vec4 (density, velocity xy, reserved) +
        /// vec4 (dye rgb, reserved) (std430).</summary>
        internal const uint StateBytes = GridWidth * GridHeight * 32;

        /// <summary>Ping-pong iterations recorded per rendered frame. The simulation
        /// advances one physics step per dispatch, and one step per 46fps frame evolves
        /// too slowly to feel alive; three gives ~140 steps/second — which also sets the
        /// CFL headroom for the compressible fluid's sound speed (see fluid.comp).</summary>
        internal const int Substeps = 3;

        private readonly Vk _vk;
        private readonly Device _device;

        private readonly Buffer[] _stateBuffers = new Buffer[2];
        private readonly DeviceMemory[] _stateMemories = new DeviceMemory[2];
        private PipelineLayout _pipelineLayout;
        private ShaderModule _shaderModule;
        private Pipeline _pipeline;

        private bool _stateInitialized;

        /// <summary>
        /// Which ping-pong orientation the NEXT substep uses (parity 0 ⇒ binding 2 =
        /// buffer A, binding 3 = buffer B; parity 1 is the reverse). Advanced by each
        /// substep inside <see cref="Record"/>, which returns the last substep's parity
        /// for the host's graphics bind. Render-thread only.
        /// </summary>
        public int Parity { get; private set; }

        public FluidSimulationPass(
            Vk vk, Device device, PhysicalDeviceMemoryProperties memProps,
            DescriptorSetLayout setLayout, byte[] computeSpirv, uint pushConstantSize)
        {
            _vk = vk;
            _device = device;

            // Two device-local state buffers; TransferDst so the one-time zero fill works.
            for (var i = 0; i < 2; i++)
            {
                var bufCI = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = StateBytes,
                    Usage = BufferUsageFlags.StorageBufferBit | BufferUsageFlags.TransferDstBit,
                    SharingMode = SharingMode.Exclusive,
                };
                _vk.CreateBuffer(_device, bufCI, null, out _stateBuffers[i]);
                _vk.GetBufferMemoryRequirements(_device, _stateBuffers[i], out var memReq);
                var allocInfo = new MemoryAllocateInfo
                {
                    SType = StructureType.MemoryAllocateInfo,
                    AllocationSize = memReq.Size,
                    MemoryTypeIndex = GraphicsHost.FindMemoryType(
                        memReq.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit, memProps),
                };
                _vk.AllocateMemory(_device, allocInfo, null, out _stateMemories[i]);
                _vk.BindBufferMemory(_device, _stateBuffers[i], _stateMemories[i], 0);
            }

            // Compute pipeline: same descriptor set layout as the graphics pipeline (the
            // sets are shared), its own push range because the graphics one is
            // fragment-stage-only. Created at startup only — runtime pipeline creation is
            // avoided project-wide (MoltenVK hangs).
            fixed (byte* pCode = computeSpirv)
            {
                var smCI = new ShaderModuleCreateInfo
                {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint)computeSpirv.Length,
                    PCode = (uint*)pCode,
                };
                _vk.CreateShaderModule(_device, smCI, null, out _shaderModule);
            }

            var pushRange = new PushConstantRange(ShaderStageFlags.ComputeBit, 0, pushConstantSize);
            var plCI = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &setLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushRange,
            };
            _vk.CreatePipelineLayout(_device, plCI, null, out _pipelineLayout);

            var entryPoint = SilkMarshal.StringToPtr("main");
            try
            {
                var stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = _shaderModule,
                    PName = (byte*)entryPoint,
                };
                var cpCI = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = stage,
                    Layout = _pipelineLayout,
                };
                _vk.CreateComputePipelines(_device, default, 1, &cpCI, null, out _pipeline);
            }
            finally
            {
                SilkMarshal.Free(entryPoint);
            }
        }

        /// <summary>
        /// Writes the state buffers into the host's descriptor sets: for each frame slot,
        /// the parity-0 set gets (binding 2 = A, binding 3 = B) and the parity-1 set the
        /// reverse. Called once at startup; the sets are never rewritten afterwards.
        /// </summary>
        public void WriteStateDescriptors(DescriptorSet[] frameSets, int frameSlots)
        {
            for (var slot = 0; slot < frameSlots; slot++)
            {
                for (var parity = 0; parity < 2; parity++)
                {
                    var src = new DescriptorBufferInfo
                    {
                        Buffer = _stateBuffers[parity],
                        Offset = 0,
                        Range = StateBytes,
                    };
                    var dst = new DescriptorBufferInfo
                    {
                        Buffer = _stateBuffers[1 - parity],
                        Offset = 0,
                        Range = StateBytes,
                    };
                    var writes = stackalloc WriteDescriptorSet[2];
                    writes[0] = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = frameSets[slot * 2 + parity],
                        DstBinding = 2,
                        DescriptorType = DescriptorType.StorageBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &src,
                    };
                    writes[1] = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = frameSets[slot * 2 + parity],
                        DstBinding = 3,
                        DescriptorType = DescriptorType.StorageBuffer,
                        DescriptorCount = 1,
                        PBufferInfo = &dst,
                    };
                    _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
                }
            }
        }

        /// <summary>
        /// Records this frame's <see cref="Substeps"/> ping-pong simulation steps. Must be
        /// called OUTSIDE the render pass (buffer barriers inside a pass would require
        /// subpass self-dependencies the pass does not declare), with BOTH of the frame
        /// slot's descriptor sets so each substep can bind its own orientation. Returns
        /// the parity of the LAST substep — the set the graphics pass must bind, because
        /// that set's binding 3 is the buffer the final substep wrote.
        /// </summary>
        public int Record<TPush>(CommandBuffer cmd, in TPush push,
            DescriptorSet setParity0, DescriptorSet setParity1)
            where TPush : unmanaged
        {
            // One-time zero of both state buffers, so the first frame advects from a clean
            // field rather than uninitialized device memory.
            if (!_stateInitialized)
            {
                var fillBarriers = stackalloc BufferMemoryBarrier[2];
                for (var i = 0; i < 2; i++)
                {
                    _vk.CmdFillBuffer(cmd, _stateBuffers[i], 0, StateBytes, 0);
                    fillBarriers[i] = new BufferMemoryBarrier
                    {
                        SType = StructureType.BufferMemoryBarrier,
                        SrcAccessMask = AccessFlags.TransferWriteBit,
                        DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                        SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                        DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                        Buffer = _stateBuffers[i],
                        Offset = 0,
                        Size = StateBytes,
                    };
                }
                _vk.CmdPipelineBarrier(cmd,
                    PipelineStageFlags.TransferBit, PipelineStageFlags.ComputeShaderBit,
                    0, 0, null, 2, fillBarriers, 0, null);
                _stateInitialized = true;
            }

            _vk.CmdBindPipeline(cmd, PipelineBindPoint.Compute, _pipeline);
            fixed (TPush* pPush = &push)
            {
                _vk.CmdPushConstants(cmd, _pipelineLayout, ShaderStageFlags.ComputeBit,
                    0, (uint)Unsafe.SizeOf<TPush>(), pPush);
            }

            var lastParity = Parity;
            for (var step = 0; step < Substeps; step++)
            {
                // Per-substep barrier: both buffers, prior fragment reads AND prior
                // compute access → this compute. On the first substep it covers the
                // write-after-read hazard on the new dst (an earlier frame's viz read it)
                // across the 3 frames in flight; on later substeps it orders the previous
                // dispatch's writes before this one's reads. Do not narrow to one buffer.
                var preBarriers = stackalloc BufferMemoryBarrier[2];
                for (var i = 0; i < 2; i++)
                {
                    preBarriers[i] = new BufferMemoryBarrier
                    {
                        SType = StructureType.BufferMemoryBarrier,
                        SrcAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                        DstAccessMask = AccessFlags.ShaderReadBit | AccessFlags.ShaderWriteBit,
                        SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                        DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                        Buffer = _stateBuffers[i],
                        Offset = 0,
                        Size = StateBytes,
                    };
                }
                _vk.CmdPipelineBarrier(cmd,
                    PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit,
                    PipelineStageFlags.ComputeShaderBit,
                    0, 0, null, 2, preBarriers, 0, null);

                var set = Parity == 0 ? setParity0 : setParity1;
                _vk.CmdBindDescriptorSets(cmd, PipelineBindPoint.Compute, _pipelineLayout,
                    0, 1, &set, 0, null);
                _vk.CmdDispatch(cmd, GridWidth / 16, GridHeight / 16, 1);

                lastParity = Parity;
                Parity = 1 - Parity;
            }

            // Final barrier: the last dispatch's writes to its dst → this frame's
            // fragment reads. The graphics pass binds the lastParity set, whose
            // binding 3 is exactly that buffer.
            var dstBuffer = _stateBuffers[1 - lastParity];
            var postBarrier = new BufferMemoryBarrier
            {
                SType = StructureType.BufferMemoryBarrier,
                SrcAccessMask = AccessFlags.ShaderWriteBit,
                DstAccessMask = AccessFlags.ShaderReadBit,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Buffer = dstBuffer,
                Offset = 0,
                Size = StateBytes,
            };
            _vk.CmdPipelineBarrier(cmd,
                PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.FragmentShaderBit,
                0, 0, null, 1, &postBarrier, 0, null);

            return lastParity;
        }

        /// <summary>Destroys the pass's GPU objects. Follows the host's existing shutdown
        /// pattern (called from the same place the other descriptor resources go).</summary>
        public void Destroy()
        {
            if (_pipeline.Handle != 0) { _vk.DestroyPipeline(_device, _pipeline, null); _pipeline = default; }
            if (_pipelineLayout.Handle != 0) { _vk.DestroyPipelineLayout(_device, _pipelineLayout, null); _pipelineLayout = default; }
            if (_shaderModule.Handle != 0) { _vk.DestroyShaderModule(_device, _shaderModule, null); _shaderModule = default; }
            for (var i = 0; i < 2; i++)
            {
                if (_stateBuffers[i].Handle != 0) { _vk.DestroyBuffer(_device, _stateBuffers[i], null); _stateBuffers[i] = default; }
                if (_stateMemories[i].Handle != 0) { _vk.FreeMemory(_device, _stateMemories[i], null); _stateMemories[i] = default; }
            }
        }
    }
}
