using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Verse;

namespace EdB.PrepareCarefully {
    // An editor checkpoint, not a portable preset. Keep external references to the
    // actual world objects; resolve deep-saved objects against the restored graph.
    public sealed class PawnEditBackup {
        private readonly List<Pawn> pawns;
        private readonly ScribeDataSnapshot snapshot;

        public PawnEditBackup(IEnumerable<Pawn> pawns) {
            this.pawns = pawns.ToList();
            snapshot = ScribeDataSnapshot.Capture(ExposePawns);
        }

        public void Restore() {
            snapshot.Restore(ExposePawns);
            foreach (var pawn in pawns) {
                pawn.ClearCaches();
            }
        }

        private void ExposePawns() {
            for (int i = 0; i < pawns.Count; i++) {
                if (!Scribe.EnterNode("pawn" + i)) {
                    throw new InvalidDataException("Missing pawn in editor checkpoint");
                }
                try {
                    var pawn = pawns[i];
                    if (Scribe.mode == LoadSaveMode.LoadingVars) {
                        Scribe.loader.crossRefs.RegisterForCrossRefResolve(pawn);
                        Scribe.loader.initer.RegisterForPostLoadInit(pawn);
                        Scribe.loader.curParent = pawn;
                        Scribe.loader.curPathRelToParent = null;
                    }
                    pawn.ExposeData();
                }
                finally {
                    if (Scribe.mode == LoadSaveMode.LoadingVars) {
                        Scribe.loader.curParent = null;
                        Scribe.loader.curPathRelToParent = null;
                    }
                    Scribe.ExitNode();
                }
            }
        }
    }

    internal sealed class ScribeDataSnapshot {
        public string Xml { get; private set; }
        public Dictionary<string, ILoadReferenceable> References { get; } = new Dictionary<string, ILoadReferenceable>();
        public HashSet<string> DeepSavedIds { get; } = new HashSet<string>();
        public List<ILoadReferenceable> DeepSavedObjects { get; } = new List<ILoadReferenceable>();
        private static ScribeDataSnapshot capturing;

        public static ScribeDataSnapshot Capture(Action expose) {
            var result = new ScribeDataSnapshot();
            var harmony = new Harmony("EdB.PrepareCarefully.EditorCheckpoint");
            var referencedMethod = AccessTools.Method(typeof(DebugLoadIDsSavingErrorsChecker), "RegisterReferenced");
            var deepMethod = AccessTools.Method(typeof(DebugLoadIDsSavingErrorsChecker), "RegisterDeepSaved");
            RunIsolated(path => {
                try {
                    capturing = result;
                    harmony.Patch(referencedMethod, prefix: new HarmonyMethod(typeof(ScribeDataSnapshot), nameof(RecordReference)));
                    harmony.Patch(deepMethod, prefix: new HarmonyMethod(typeof(ScribeDataSnapshot), nameof(RecordDeepSaved)));
                    Scribe.saver.InitSaving(path, "checkpoint");
                    expose();
                    // External references intentionally aren't deep-saved in an editor checkpoint.
                    Scribe.saver.loadIDsErrorsChecker.Clear();
                    Scribe.saver.FinalizeSaving();
                    result.Xml = File.ReadAllText(path);
                }
                finally {
                    capturing = null;
                    harmony.Unpatch(referencedMethod, HarmonyPatchType.Prefix, harmony.Id);
                    harmony.Unpatch(deepMethod, HarmonyPatchType.Prefix, harmony.Id);
                }
            });
            return result;
        }

        public static ScribeDataSnapshot FromXml(string xml, Dictionary<string, ILoadReferenceable> references) {
            var result = new ScribeDataSnapshot() { Xml = xml };
            foreach (var pair in references) {
                result.References.Add(pair.Key, pair.Value);
            }
            return result;
        }

        public void Restore(Action expose, Action<List<IExposable>> afterLoad = null) {
            RunIsolated(path => {
                File.WriteAllText(path, Xml);
                Scribe.loader.InitLoading(path);
                expose();
                var loaded = Scribe.loader.crossRefs.crossReferencingExposables.ToList();
                var loadedIds = new HashSet<string>(loaded.OfType<ILoadReferenceable>().Select(x => x.GetUniqueLoadID()));
                var directory = AccessTools.Field(typeof(CrossRefHandler), "loadedObjectDirectory")
                    .GetValue(Scribe.loader.crossRefs) as LoadedObjectDirectory;
                foreach (var pair in References) {
                    if (!loadedIds.Contains(pair.Key)) {
                        if (pair.Key == pair.Value.GetUniqueLoadID()) {
                            directory.RegisterLoaded(pair.Value);
                        }
                        else {
                            // A portable preset's old owner ID must resolve to its new pawn.
                            var objects = (Dictionary<string, ILoadReferenceable>)AccessTools
                                .Field(typeof(LoadedObjectDirectory), "allObjectsByLoadID").GetValue(directory);
                            objects.Add(pair.Key, pair.Value);
                        }
                    }
                }
                Scribe.loader.FinalizeLoading();
                afterLoad?.Invoke(loaded);
            });
        }

        private static void RecordReference(object __0) {
            if (capturing != null && __0 is ILoadReferenceable reference) {
                capturing.References[reference.GetUniqueLoadID()] = reference;
            }
        }

        private static void RecordDeepSaved(object __0) {
            if (capturing != null && __0 is ILoadReferenceable reference) {
                capturing.DeepSavedIds.Add(reference.GetUniqueLoadID());
                capturing.DeepSavedObjects.Add(reference);
            }
        }

        private static void RunIsolated(Action<string> action) {
            if (Scribe.mode != LoadSaveMode.Inactive) {
                throw new InvalidOperationException("Cannot checkpoint pawns during another save/load operation");
            }
            var saver = Scribe.saver;
            var loader = Scribe.loader;
            string path = Path.GetTempFileName();
            Scribe.saver = new ScribeSaver();
            Scribe.loader = new ScribeLoader();
            try {
                action(path);
            }
            finally {
                Scribe.ForceStop();
                Scribe.saver = saver;
                Scribe.loader = loader;
                File.Delete(path);
            }
        }
    }
}
