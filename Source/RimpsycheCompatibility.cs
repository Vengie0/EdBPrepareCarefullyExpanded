using RimWorld;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace EdB.PrepareCarefully {
    public static class RimpsycheCompatibility {
        private const string PersonalityCorePackageId = "maux36.rimpsyche";
        private const string DispositionPackageId = "maux36.rimpsyche.disposition";
        private const string PsycheDataUtilTypeName = "Maux36.RimPsyche.PsycheDataUtil";
        private const string CompPsycheTypeName = "Maux36.RimPsyche.CompPsyche";
        private const string RimpsycheUiUtilityTypeName = "Maux36.RimPsyche.Rimpsyche_UI_Utility";

        private static Type psycheDataUtilType;
        private static MethodInfo getSerializedDataMethod;
        private static MethodInfo injectSerializedDataMethod;
        private static MethodInfo drawEditButtonMethod;
        private static bool apiLookupComplete;
        private static bool warnedAboutMissingApi;

        public static bool IsActive {
            get {
                return ModLister.GetActiveModWithIdentifier(PersonalityCorePackageId, ignorePostfix: true) != null
                    && ModLister.GetActiveModWithIdentifier(DispositionPackageId, ignorePostfix: true) != null;
            }
        }

        public static string Capture(Pawn pawn) {
            if (!IsActive || pawn == null || !TryResolveApi()) {
                return null;
            }
            try {
                return getSerializedDataMethod.Invoke(null, new object[] { pawn, true }) as string;
            }
            catch (Exception e) {
                WarnApiFailure("capture", e);
                return null;
            }
        }

        public static void Restore(Pawn pawn, string serializedData) {
            if (!IsActive || pawn == null || string.IsNullOrEmpty(serializedData) || !TryResolveApi()) {
                return;
            }
            try {
                ParameterInfo[] parameters = injectSerializedDataMethod.GetParameters();
                object[] args = parameters.Length == 4
                    ? new object[] { pawn, serializedData, true, true }
                    : new object[] { pawn, serializedData, true };
                injectSerializedDataMethod.Invoke(null, args);
            }
            catch (Exception e) {
                WarnApiFailure("restore", e);
            }
        }

        public static bool CanEdit(Pawn pawn) {
            if (!IsActive || pawn == null) {
                return false;
            }
            foreach (ThingComp comp in pawn.AllComps) {
                if (comp.GetType().FullName != CompPsycheTypeName) {
                    continue;
                }
                PropertyInfo enabledProperty = comp.GetType().GetProperty("Enabled", BindingFlags.Public | BindingFlags.Instance);
                return enabledProperty != null && (bool)enabledProperty.GetValue(comp);
            }
            return false;
        }

        public static void DrawEditButton(Rect rect, Pawn pawn) {
            if (!CanEdit(pawn) || !TryResolveEditButton()) {
                return;
            }
            try {
                drawEditButtonMethod.Invoke(null, new object[] { rect, pawn });
            }
            catch (Exception e) {
                WarnApiFailure("open the personality editor", e);
            }
        }

        private static bool TryResolveEditButton() {
            if (drawEditButtonMethod != null) {
                return true;
            }
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                Type utilityType = assembly.GetType(RimpsycheUiUtilityTypeName, false);
                if (utilityType == null) {
                    continue;
                }
                drawEditButtonMethod = utilityType.GetMethod(
                    "DrawEditButton",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Rect), typeof(Pawn) },
                    null);
                break;
            }
            if (drawEditButtonMethod == null && !warnedAboutMissingApi) {
                warnedAboutMissingApi = true;
                Logger.Warning("Rimpsyche compatibility is enabled, but its personality editor button could not be found.");
            }
            return drawEditButtonMethod != null;
        }

        private static bool TryResolveApi() {
            if (!apiLookupComplete) {
                apiLookupComplete = true;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    psycheDataUtilType = assembly.GetType(PsycheDataUtilTypeName, false);
                    if (psycheDataUtilType != null) {
                        break;
                    }
                }
                if (psycheDataUtilType != null) {
                    getSerializedDataMethod = psycheDataUtilType.GetMethod(
                        "GetSerializedStringPsycheData",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Pawn), typeof(bool) },
                        null);
                    injectSerializedDataMethod = psycheDataUtilType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(method => method.Name == "InjectSerializedStringPsycheData")
                        .OrderByDescending(method => method.GetParameters().Length)
                        .FirstOrDefault(method => method.GetParameters().Length == 3 || method.GetParameters().Length == 4);
                }
            }

            if (getSerializedDataMethod == null || injectSerializedDataMethod == null) {
                if (!warnedAboutMissingApi) {
                    warnedAboutMissingApi = true;
                    Logger.Warning("Rimpsyche compatibility is enabled, but its psyche data API could not be found. Pawn personality data will be left as generated.");
                }
                return false;
            }
            return true;
        }

        private static void WarnApiFailure(string operation, Exception exception) {
            if (!warnedAboutMissingApi) {
                warnedAboutMissingApi = true;
                Logger.Warning("Rimpsyche compatibility could not " + operation + " pawn personality data.\n" + exception);
            }
        }
    }
}
