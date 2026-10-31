using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using RimWorld;
using Verse;

namespace EdB.PrepareCarefully {
    // Portable health data must not retain references to a different game's objects.
    public sealed class PresetHealthState : IExposable {
        private string xml;
        private string ownerId;

        public void ExposeData() {
            Scribe_Values.Look(ref xml, "xml");
            Scribe_Values.Look(ref ownerId, "ownerId");
        }

        public static PresetHealthState Capture(Pawn pawn) {
            var health = pawn.health;
            var snapshot = ScribeDataSnapshot.Capture(() => Scribe_Deep.Look(ref health, "health", pawn));
            foreach (var pair in snapshot.References) {
                if (pair.Value != pawn && !snapshot.DeepSavedIds.Contains(pair.Key)) {
                    throw new InvalidOperationException("Cannot safely export health for " + pawn.LabelShort
                        + ": a condition refers to an external world object (" + pair.Key + ").");
                }
            }
            if (snapshot.DeepSavedObjects.Any(obj => !(obj is Hediff) && !(obj is Ability))) {
                throw new InvalidOperationException("Cannot safely export health for " + pawn.LabelShort
                    + ": it contains an object whose unique ID cannot be renewed on import.");
            }
            return new PresetHealthState() { xml = snapshot.Xml, ownerId = pawn.GetUniqueLoadID() };
        }

        public void Apply(Pawn pawn) {
            var document = new XmlDocument();
            document.LoadXml(xml);
            var nodes = document.SelectNodes("/checkpoint/health/hediffSet/hediffs/li");
            if (nodes == null) {
                throw new InvalidDataException("Missing health data in preset");
            }
            foreach (XmlNode node in nodes) {
                string defName = node["def"]?.InnerText;
                if (defName == null || DefDatabase<HediffDef>.GetNamedSilentFail(defName) == null) {
                    throw new InvalidDataException("A health condition in the preset requires a missing definition: " + defName);
                }
            }
            Pawn_HealthTracker restored = null;
            var references = new Dictionary<string, ILoadReferenceable>();
            if (!string.IsNullOrEmpty(ownerId)) {
                references.Add(ownerId, pawn);
            }
            var snapshot = ScribeDataSnapshot.FromXml(xml, references);
            foreach (var hediff in pawn.health.hediffSet.hediffs.ToList()) {
                if (pawn.health.hediffSet.hediffs.Contains(hediff)) {
                    pawn.health.RemoveHediff(hediff);
                }
            }
            snapshot.Restore(() => {
                Scribe_Deep.Look(ref restored, "health", pawn);
                pawn.health = restored;
            }, loaded => {
                if (restored == null || restored.hediffSet.hediffs.Count != nodes.Count) {
                    throw new InvalidDataException("Not all health conditions in the preset could be restored");
                }
                foreach (var hediff in restored.hediffSet.hediffs) {
                    if (hediff.def == null || (hediff.Part != null && !pawn.RaceProps.body.AllParts.Contains(hediff.Part))) {
                        throw new InvalidDataException("A health condition in the preset does not match the pawn's body");
                    }
                }
                foreach (var obj in loaded.OfType<ILoadReferenceable>()) {
                    if (obj is Hediff hediff) {
                        hediff.loadID = Find.UniqueIDsManager.GetNextHediffID();
                    }
                    else if (obj is Ability ability) {
                        ability.Id = Find.UniqueIDsManager.GetNextAbilityID();
                    }
                    else {
                        throw new InvalidDataException("Unsupported object ID in preset health data");
                    }
                }
            });
            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
        }
    }
}
