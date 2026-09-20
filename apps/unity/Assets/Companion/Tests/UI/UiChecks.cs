using System;
using System.Reflection;
using System.Runtime.InteropServices;
using AICompanion.Preview.UI;
using UnityEngine;

namespace AICompanion.Preview.Tests
{
    public static class UiChecks
    {
        private static int assertions;
        public static void Run()
        {
            assertions = 0;
            Check(EnterPolicy.ShouldSubmit(true, false, false, 3), "plain Return submits");
            Check(!EnterPolicy.ShouldSubmit(true, true, false, 10), "Shift Return remains newline");
            Check(!EnterPolicy.ShouldSubmit(true, false, true, 0), "IME candidates do not send");
            Check(!EnterPolicy.ShouldSubmit(true, false, false, 1), "IME commit Return is guarded");
            Check(!EnterPolicy.ShouldSubmit(false, false, false, 20), "holding Return cannot repeat");
            Check(EnterPolicy.CountScalars("你好🦊\n") == 4, "Unicode scalar counting");
            Check(EnterPolicy.CountScalars("\ud800") == int.MaxValue, "unpaired surrogate rejected");
            Check(EnterPolicy.IsReturnCharacter('\r') && EnterPolicy.IsReturnCharacter('\n') && EnterPolicy.IsReturnCharacter('\v') && EnterPolicy.IsReturnCharacter('\u0003'), "native duplicate Return characters are suppressed");
            Check(!EnterPolicy.IsReturnCharacter('中') && !EnterPolicy.IsReturnCharacter('\t'), "ordinary text and Tab do not match duplicate Return characters");
            var gate = new ReturnKeyGate();
            Check(gate.Observe(EventType.KeyDown, EventModifiers.Shift, false, 20, 50) == ReturnKeyAction.Newline, "native Shift Return remains newline after frame modifier release");
            Check(gate.Observe(EventType.KeyUp, EventModifiers.None, false, 20, 50) == ReturnKeyAction.None, "same-frame native key up only releases latch");
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, false, 20, 51) == ReturnKeyAction.Send, "bare native Return sends once");
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, false, 20, 52) == ReturnKeyAction.None, "native autorepeat does not send again");
            gate.Observe(EventType.KeyUp, EventModifiers.None, false, 20, 52);
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, true, 0, 53) == ReturnKeyAction.None, "native IME confirmation is suppressed");
            gate.Observe(EventType.KeyUp, EventModifiers.None, false, 1, 54);
            Check(gate.Observe(EventType.KeyDown, EventModifiers.None, false, 1, 54) == ReturnKeyAction.None, "native post-composition Return is suppressed");
#if UNITY_EDITOR_WIN
            var nativeType = typeof(HistoryFileExport).GetNestedType("OpenFileName", BindingFlags.NonPublic);
            int nativeSize = Marshal.SizeOf(nativeType);
            Check(nativeSize == (IntPtr.Size == 8 ? 152 : 88), "OPENFILENAME matches Windows ABI");
            var memory = Marshal.AllocHGlobal(nativeSize);
            try
            {
                Marshal.StructureToPtr(Activator.CreateInstance(nativeType), memory, false);
                Check(Marshal.PtrToStructure(memory, nativeType) != null, "native dialog descriptor marshals on actual Unity Mono");
            }
            finally { Marshal.FreeHGlobal(memory); }
#endif
            Debug.Log("U01_UI_CHECKS_PASSED assertions=" + assertions + "; actual Windows IME/DPI requires Player QA");
        }
        private static void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception("UI check failed: " + name); }
    }
}
