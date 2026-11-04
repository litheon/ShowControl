using System;

namespace ShowControl.Devices.Audio
{
    /// <summary>
    /// Marks a static method as the target of a native-to-managed callback so the Mono AOT
    /// compiler emits its reverse-P/Invoke wrapper ahead of time. The published Mac Catalyst
    /// arm64 app runs aot-only: without the wrapper, <c>Marshal.GetFunctionPointerForDelegate</c>
    /// throws <c>ExecutionEngineException</c> ("Attempting to JIT compile method ... while
    /// running in aot-only mode") and audio capture never opens. Mono matches this attribute
    /// by type name alone, which is why a local definition works for the plain net9.0 target
    /// where <c>ObjCRuntime.MonoPInvokeCallbackAttribute</c> does not exist. The target must be
    /// static; instance methods have no precompiled wrapper, so the delegates handed to
    /// PortAudioSharp are static trampolines that find the service through the stream's
    /// user-data handle.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type delegateType) { DelegateType = delegateType; }
        public Type DelegateType { get; }
    }
}
