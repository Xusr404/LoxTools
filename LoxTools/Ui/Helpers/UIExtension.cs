using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestApplicationsContext.Utilities {
    static class Uiextension {
        public static void InvokeIfRequired(this System.ComponentModel.ISynchronizeInvoke obj, System.Windows.Forms.MethodInvoker action, bool async = false) {
            if (obj?.InvokeRequired ?? false) {
                var args = new object[0];
                bool actionFailed = false;
                // wrap action to get flag if exception is thrown by the action
                Action internalAction = () => {
                    try {
                        action();
                    }
                    catch {
                        actionFailed = true;
                        throw;
                    }
                };
                try {
                    if (async) {
                        obj.BeginInvoke(internalAction, args);
                    }
                    else {
                        obj.Invoke(internalAction, args);
                    }
                }
                catch (ObjectDisposedException) when (!actionFailed) { }
                catch (InvalidOleVariantTypeException) when (!actionFailed) { }
            } else {
                action();
			}
        }
    }
}
