using System;using System.Runtime.InteropServices;
namespace CFEditor {
// Thin WinMM ABI only. No static discovery/open and no device/USB-port reset.
// Reset returns buffers from handles this owner opened; never global handles.
sealed class WinmmAssetPortDriver:IAssetPortDriver {
 static uint Size{get{return (uint)Marshal.SizeOf(typeof(AssetMidiHeader));}}
 [DllImport("winmm.dll")]static extern uint midiInOpen(out IntPtr handle,uint id,UIntPtr callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")]static extern uint midiOutOpen(out IntPtr handle,uint id,UIntPtr callback,UIntPtr instance,uint flags);
 [DllImport("winmm.dll")]static extern uint midiInPrepareHeader(IntPtr h,IntPtr header,uint size);
 [DllImport("winmm.dll")]static extern uint midiOutPrepareHeader(IntPtr h,IntPtr header,uint size);
 [DllImport("winmm.dll")]static extern uint midiInUnprepareHeader(IntPtr h,IntPtr header,uint size);
 [DllImport("winmm.dll")]static extern uint midiOutUnprepareHeader(IntPtr h,IntPtr header,uint size);
 [DllImport("winmm.dll")]static extern uint midiInAddBuffer(IntPtr h,IntPtr header,uint size);
 [DllImport("winmm.dll")]static extern uint midiOutLongMsg(IntPtr h,IntPtr header,uint size);
 [DllImport("winmm.dll")]static extern uint midiInStart(IntPtr h);
 [DllImport("winmm.dll")]static extern uint midiInStop(IntPtr h);
 [DllImport("winmm.dll")]static extern uint midiInReset(IntPtr h);
 [DllImport("winmm.dll")]static extern uint midiOutReset(IntPtr h);
 [DllImport("winmm.dll")]static extern uint midiInClose(IntPtr h);
 [DllImport("winmm.dll")]static extern uint midiOutClose(IntPtr h);
 public uint OpenInput(uint id,out IntPtr h){return midiInOpen(out h,id,UIntPtr.Zero,UIntPtr.Zero,0);}
 public uint OpenOutput(uint id,out IntPtr h){return midiOutOpen(out h,id,UIntPtr.Zero,UIntPtr.Zero,0);}
 public void Revalidate(AssetPortBinding binding,bool input,IntPtr handle){AssetPortDiscovery.Revalidate(binding,input,handle);}
 public uint Prepare(bool input,IntPtr h,AssetMidiBuffer b){return input?midiInPrepareHeader(h,b.Header,Size):midiOutPrepareHeader(h,b.Header,Size);}
 public uint Unprepare(bool input,IntPtr h,AssetMidiBuffer b){return input?midiInUnprepareHeader(h,b.Header,Size):midiOutUnprepareHeader(h,b.Header,Size);}
 public uint QueueInput(IntPtr h,AssetMidiBuffer b){return midiInAddBuffer(h,b.Header,Size);}
 public uint StartInput(IntPtr h){return midiInStart(h);}public uint StopInput(IntPtr h){return midiInStop(h);}
 public uint Reset(bool input,IntPtr h){return input?midiInReset(h):midiOutReset(h);}
 public uint Close(bool input,IntPtr h){return input?midiInClose(h):midiOutClose(h);}
 public uint Send(IntPtr h,AssetMidiBuffer b){return midiOutLongMsg(h,b.Header,Size);}
}
}
