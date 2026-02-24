using System.Collections.Generic;
using NUnit.Framework;
using Odengine.Core;
using Odengine.Coupling;
using Odengine.Fields;

namespace Odengine.Tests.Modules.Coupling
{
    [TestFixture]
    public class Coupling_CouplingRuleTests
    {
        // ── Helpers ────────────────────────────────────────────────────────────

        private static FieldProfile MakeProfile(string id, float decay = 0f) =>
            new FieldProfile(id)
            {
                LogEpsilon    = 0.0001f,
                MinLogAmpClamp = -10f,
                MaxLogAmpClamp =  10f,
                DecayRate     = decay,
            };

        private static Dimension MakeDimension(params string[] nodeIds)
        {
            var dim = new Dimension();
            foreach (var id in nodeIds) dim.AddNode(id);
            return dim;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Operator: Linear
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Operator_Linear_ScalesInput()
        {
            Assert.That(CouplingOperator.Linear(2f).Apply(3f), Is.EqualTo(6f).Within(1e-6f));
        }

        [Test]
        public void Operator_Linear_NegativeScaleInverts()
        {
            Assert.That(CouplingOperator.Linear(-0.5f).Apply(4f), Is.EqualTo(-2f).Within(1e-6f));
        }

        [Test]
        public void Operator_Linear_BiasAddedToScaledInput()
        {
            Assert.That(CouplingOperator.Linear(1f, bias: 0.5f).Apply(2f), Is.EqualTo(2.5f).Within(1e-6f));
        }

        [Test]
        public void Operator_Linear_ZeroInput_ReturnsBiasOnly()
        {
            Assert.That(CouplingOperator.Linear(3f, bias: 1f).Apply(0f), Is.EqualTo(1f).Within(1e-6f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Operator: Clamp
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Operator_Clamp_ClampsAboveMax()
        {
            Assert.That(CouplingOperator.Clamp(0f, 2f).Apply(5f), Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void Operator_Clamp_ClampsBelowMin()
        {
            Assert.That(CouplingOperator.Clamp(1f, 5f).Apply(-3f), Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void Operator_Clamp_InRange_ScalesDirectly()
        {
            Assert.That(CouplingOperator.Clamp(0f, 10f, scale: 0.5f).Apply(4f), Is.EqualTo(2f).Within(1e-6f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Operator: Threshold
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Operator_Threshold_FiresAboveThreshold()
        {
            Assert.That(CouplingOperator.Threshold(1f, 3f).Apply(2f), Is.EqualTo(3f).Within(1e-6f));
        }

        [Test]
        public void Operator_Threshold_SilentAtOrBelowThreshold()
        {
            var op = CouplingOperator.Threshold(1f, 3f);
            Assert.That(op.Apply(1.0f), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(op.Apply(0.5f), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Operator_Threshold_NegativeImpulse()
        {
            Assert.That(CouplingOperator.Threshold(0.5f, -1f).Apply(1f), Is.EqualTo(-1f).Within(1e-6f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Operator: Ratio
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Operator_Ratio_DividesByReference()
        {
            Assert.That(CouplingOperator.Ratio(4f).Apply(8f), Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void Operator_Ratio_ZeroReferenceReturnsZero()
        {
            Assert.That(CouplingOperator.Ratio(0f).Apply(5f), Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void Operator_Ratio_ScaleAppliedAfterDivision()
        {
            // (4 / 2) × 3 = 6
            Assert.That(CouplingOperator.Ratio(2f, scale: 3f).Apply(4f), Is.EqualTo(6f).Within(1e-6f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CouplingProcessor: guard conditions
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Step_EmptyRuleList_NoThrow()
        {
            var dim = MakeDimension("a");
            Assert.DoesNotThrow(() => CouplingProcessor.Step(dim, new List<CouplingRule>(), 1f));
        }

        [Test]
        public void Step_ZeroDeltaTime_NoInjection()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 2f);
            dst.AddLogAmp("a", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst") { InputChannelSelector = "x", OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(1f), ScaleByDeltaTime = true }
            };

            CouplingProcessor.Step(dim, rules, 0f);

            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Step_MissingSourceField_SilentlySkipped()
        {
            var dim = MakeDimension("a");
            var dst = dim.AddField("dst", MakeProfile("dp"));
            dst.AddLogAmp("a", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("nonexistent", "dst")
                    { InputChannelSelector = "*", OutputChannelSelector = "*",
                      Operator = CouplingOperator.Linear(99f) }
            };

            Assert.DoesNotThrow(() => CouplingProcessor.Step(dim, rules, 1f));
            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Step_MissingTargetField_SilentlySkipped()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            src.AddLogAmp("a", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "nonexistent")
                    { InputChannelSelector = "*", OutputChannelSelector = "same",
                      Operator = CouplingOperator.Linear(1f) }
            };

            Assert.DoesNotThrow(() => CouplingProcessor.Step(dim, rules, 1f));
        }

        [Test]
        public void Step_ZeroSourceLogAmp_NoInjection()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            // src: no entries (all logAmps = neutral 0)
            dst.AddLogAmp("a", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                    { InputChannelSelector = "*", OutputChannelSelector = "same",
                      Operator = CouplingOperator.Linear(1f) }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(1f).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // CouplingProcessor: deltaTime scaling
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Step_ScaleByDt_True_MultipliesOutputByDeltaTime()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 1f);
            dst.AddLogAmp("a", "y", 0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[y]",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = true,
                }
            };

            CouplingProcessor.Step(dim, rules, deltaTime: 0.5f);

            // src logAmp=1.0, Linear(1.0), dt=0.5 → inject 0.5 into dst "y"
            Assert.That(dst.GetLogAmp("a", "y"), Is.EqualTo(1.0f).Within(1e-4f)); // 0.5 + 0.5
        }

        [Test]
        public void Step_ScaleByDt_False_ImpulseIsConstantRegardlessOfDt()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "ch", 2f);
            dst.AddLogAmp("a", "ch", 0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "ch",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(0.3f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, deltaTime: 999f); // large dt — should NOT scale

            // raw = 2.0 × 0.3 = 0.6, no dt → dst = 0.5 + 0.6 = 1.1
            Assert.That(dst.GetLogAmp("a", "ch"), Is.EqualTo(1.1f).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Output channel selectors
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void OutputSelector_Same_InjectsIntoInputChannel()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "ore", 2f);
            dst.AddLogAmp("a", "ore",   0.5f);
            dst.AddLogAmp("a", "water", 0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "*",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "ore"),   Is.EqualTo(2.5f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("a", "water"), Is.EqualTo(0.5f).Within(1e-4f)); // untouched
        }

        [Test]
        public void OutputSelector_Star_InjectsIntoAllActiveTargetChannels()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x",     2f);
            dst.AddLogAmp("a", "ore",   1f);
            dst.AddLogAmp("a", "water", 0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "*",
                    Operator = CouplingOperator.Linear(-0.5f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // src=2.0, ×(-0.5)=-1.0 injected into BOTH "ore" and "water"
            Assert.That(dst.GetLogAmp("a", "ore"),   Is.EqualTo(0f).Within(1e-4f));   // 1.0 - 1.0
            Assert.That(dst.GetLogAmp("a", "water"), Is.EqualTo(-0.5f).Within(1e-4f)); // 0.5 - 1.0
        }

        [Test]
        public void OutputSelector_Star_DoesNotInjectIntoInactiveChannels()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 2f);
            // dst has NO active channels at "a" — "*" output should inject nothing

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "*",
                    Operator = CouplingOperator.Linear(-1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetActiveNodeIdsSorted().Count, Is.EqualTo(0)); // dst remains empty
        }

        [Test]
        public void OutputSelector_Explicit_InjectsIntoNamedChannelsOnly()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 1f);
            dst.AddLogAmp("a", "ore",   0.5f);
            dst.AddLogAmp("a", "water", 0.5f);
            dst.AddLogAmp("a", "food",  0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[ore,water]",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "ore"),   Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("a", "water"), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("a", "food"),  Is.EqualTo(0.5f).Within(1e-4f)); // untouched
        }

        [Test]
        public void OutputSelector_Explicit_CanCreateNewChannelsInTarget()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 2f);
            // dst has NO active channels — explicit will create one

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[newchannel]",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "newchannel"), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void OutputSelector_Bare_WritesToSingleChannel()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 3f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "target",
                    Operator = CouplingOperator.Linear(0.5f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "target"), Is.EqualTo(1.5f).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Input channel selectors
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void InputSelector_Star_ProcessesAllActiveChannels()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "red",  1f);
            src.AddLogAmp("a", "blue", 2f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "*",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "red"),  Is.EqualTo(1f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("a", "blue"), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void InputSelector_Explicit_OnlyProcessesNamedChannels()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 3f);
            src.AddLogAmp("a", "y", 5f); // should be ignored

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "explicit:[x]",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(3f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("a", "y"), Is.EqualTo(0f).Within(1e-4f)); // not touched
        }

        [Test]
        public void InputSelector_Bare_SkipsIfChannelInactive()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "y", 2f); // "x" is NOT active
            dst.AddLogAmp("a", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",   // look for "x" — not active in src at "a"
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // "y" is active (node "a" appears in GetActiveNodeIdsSorted)
            // but "x" logAmp at "a" = 0 < epsilon → skipped
            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void InputSelector_ExplicitShortFormat_NotracketsAlsoWorks()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "ch1", 1f);
            src.AddLogAmp("a", "ch2", 2f); // should be skipped

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "explicit:ch1",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "ch1"), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("a", "ch2"), Is.EqualTo(0f).Within(1e-4f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Double-buffering and determinism
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Step_DoubleBuffered_SelfCouplingReadsPreStepValues()
        {
            // Self-coupling: source == target.
            // Both "a" and "b" must each get +0.5 from reading the pre-step value 1.0.
            // If double-buffering is broken, "b" might read "a"'s already-modified value.
            var dim = MakeDimension("a", "b");
            var field = dim.AddField("f", MakeProfile("fp"));
            field.AddLogAmp("a", "x", 1f);
            field.AddLogAmp("b", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("f", "f")
                {
                    InputChannelSelector  = "*",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(0.5f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // Both nodes read 1.0, get +0.5 each
            Assert.That(field.GetLogAmp("a", "x"), Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(field.GetLogAmp("b", "x"), Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void Step_MultipleRules_AllApplied_ReadFromOriginalState()
        {
            var dim = MakeDimension("a");
            var war   = dim.AddField("war",   MakeProfile("wp"));
            var avail = dim.AddField("avail", MakeProfile("ap"));
            var price = dim.AddField("price", MakeProfile("pp"));

            war.AddLogAmp("a", "x", 2f);
            avail.AddLogAmp("a", "ore", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("war", "avail")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "*",
                    Operator = CouplingOperator.Linear(-0.5f),
                    ScaleByDeltaTime = false,
                },
                new CouplingRule("war", "price")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[ore]",
                    Operator = CouplingOperator.Linear(0.3f),
                    ScaleByDeltaTime = false,
                },
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // war=2.0; rule1 → avail.ore: 1.0 + 2.0×(-0.5) = 0.0
            Assert.That(avail.GetLogAmp("a", "ore"), Is.EqualTo(0f).Within(1e-4f));
            // rule2 → price.ore: 0.0 + 2.0×0.3 = 0.6
            Assert.That(price.GetLogAmp("a", "ore"), Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void Step_TwoRulesToSameTarget_DeltasAccumulate()
        {
            var dim  = MakeDimension("a");
            var srcA = dim.AddField("srcA", MakeProfile("sa"));
            var srcB = dim.AddField("srcB", MakeProfile("sb"));
            var dst  = dim.AddField("dst",  MakeProfile("dp"));

            srcA.AddLogAmp("a", "x", 1f);
            srcB.AddLogAmp("a", "x", 2f);
            dst.AddLogAmp("a",  "x", 0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("srcA", "dst") { InputChannelSelector="x", OutputChannelSelector="same",
                    Operator=CouplingOperator.Linear(1f), ScaleByDeltaTime=false },
                new CouplingRule("srcB", "dst") { InputChannelSelector="x", OutputChannelSelector="same",
                    Operator=CouplingOperator.Linear(1f), ScaleByDeltaTime=false },
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // dst.x += 1.0 (srcA) + 2.0 (srcB) = 3.0 total → 0.5 + 3.0 = 3.5
            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(3.5f).Within(1e-4f));
        }

        [Test]
        public void Step_MultipleNodes_EachProcessedIndependently()
        {
            var dim = MakeDimension("north", "south");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("north", "x", 2f);
            src.AddLogAmp("south", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[x]",
                    Operator = CouplingOperator.Linear(1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("north", "x"), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(dst.GetLogAmp("south", "x"), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Step_Determinism_TwoIdenticalRunsProduceSameResult()
        {
            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "*",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Linear(-0.3f),
                    ScaleByDeltaTime = true,
                }
            };

            Dimension BuildAndRun()
            {
                var dim = MakeDimension("north", "south", "east");
                var src = dim.AddField("src", MakeProfile("sp"));
                var dst = dim.AddField("dst", MakeProfile("dp"));
                src.AddLogAmp("north", "x", 2f);
                src.AddLogAmp("south", "x", 1f);
                dst.AddLogAmp("north", "x", 1f);
                dst.AddLogAmp("south", "x", 0.5f);
                CouplingProcessor.Step(dim, rules, 0.5f);
                return dim;
            }

            var dim1 = BuildAndRun();
            var dim2 = BuildAndRun();

            Assert.That(dim1.GetField("dst").GetLogAmp("north", "x"),
                Is.EqualTo(dim2.GetField("dst").GetLogAmp("north", "x")).Within(1e-7f));
            Assert.That(dim1.GetField("dst").GetLogAmp("south", "x"),
                Is.EqualTo(dim2.GetField("dst").GetLogAmp("south", "x")).Within(1e-7f));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Threshold via Step
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Step_Threshold_NotReached_NoInjection()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 0.4f); // below threshold 0.5
            dst.AddLogAmp("a", "x", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Threshold(0.5f, 2f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(1f).Within(1e-4f)); // unchanged
        }

        [Test]
        public void Step_Threshold_Reached_InjectsImpulse()
        {
            var dim = MakeDimension("a");
            var src = dim.AddField("src", MakeProfile("sp"));
            var dst = dim.AddField("dst", MakeProfile("dp"));
            src.AddLogAmp("a", "x", 1.5f); // above threshold 1.0
            dst.AddLogAmp("a", "x", 0.5f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("src", "dst")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "same",
                    Operator = CouplingOperator.Threshold(1.0f, -0.8f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(dst.GetLogAmp("a", "x"), Is.EqualTo(-0.3f).Within(1e-4f)); // 0.5 + (-0.8)
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Integration: War → Economy
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Integration_WarExposureReducesAvailability()
        {
            var dim = MakeDimension("north");
            var warField   = dim.AddField("war.exposure",          MakeProfile("wp"));
            var availField = dim.AddField("economy.availability",  MakeProfile("ep"));

            warField.AddLogAmp("north", "x",     3f);
            availField.AddLogAmp("north", "ore",   1f);
            availField.AddLogAmp("north", "water", 0.8f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("war.exposure", "economy.availability")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "*",
                    Operator = CouplingOperator.Linear(-0.2f),
                    ScaleByDeltaTime = true,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // war=3.0, Linear(-0.2), dt=1.0 → inject -0.6 into each active avail channel
            Assert.That(availField.GetLogAmp("north", "ore"),   Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(availField.GetLogAmp("north", "water"), Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Integration_NoWar_EconomyCompletelyUnaffected()
        {
            var dim = MakeDimension("north");
            var warField   = dim.AddField("war.exposure",         MakeProfile("wp"));
            var availField = dim.AddField("economy.availability", MakeProfile("ep"));
            availField.AddLogAmp("north", "ore", 1.5f);
            // war field has no entries

            var rules = new List<CouplingRule>
            {
                new CouplingRule("war.exposure", "economy.availability")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "*",
                    Operator = CouplingOperator.Linear(-0.3f),
                    ScaleByDeltaTime = true,
                }
            };

            for (int i = 0; i < 10; i++)
                CouplingProcessor.Step(dim, rules, 1f);

            Assert.That(availField.GetLogAmp("north", "ore"), Is.EqualTo(1.5f).Within(1e-4f));
        }

        [Test]
        public void Integration_WarRaisesPrice_AndReducesAvailability_Simultaneously()
        {
            var dim = MakeDimension("north");
            var warField   = dim.AddField("war.exposure",          MakeProfile("wp"));
            var availField = dim.AddField("economy.availability",  MakeProfile("ep"));
            var priceField = dim.AddField("economy.pricePressure", MakeProfile("pp"));

            warField.AddLogAmp("north", "x", 2f);
            availField.AddLogAmp("north", "ore", 1f);

            var rules = new List<CouplingRule>
            {
                new CouplingRule("war.exposure", "economy.availability")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "*",
                    Operator = CouplingOperator.Linear(-0.15f),
                    ScaleByDeltaTime = false,
                },
                new CouplingRule("war.exposure", "economy.pricePressure")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[ore]",
                    Operator = CouplingOperator.Linear(0.10f),
                    ScaleByDeltaTime = false,
                },
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // availability drops: 1.0 + 2.0×(-0.15) = 0.70
            Assert.That(availField.GetLogAmp("north", "ore"), Is.EqualTo(0.70f).Within(1e-4f));
            // price rises: 0.0 + 2.0×0.10 = 0.20
            Assert.That(priceField.GetLogAmp("north", "ore"), Is.EqualTo(0.20f).Within(1e-4f));
        }

        [Test]
        public void Integration_FactionPresenceDampensWarExposure()
        {
            var dim = MakeDimension("north");
            var presenceField = dim.AddField("faction.presence", MakeProfile("fp"));
            var warField      = dim.AddField("war.exposure",     MakeProfile("wp"));

            presenceField.AddLogAmp("north", "red",  2f);
            presenceField.AddLogAmp("north", "blue", 1f);
            warField.AddLogAmp("north", "x", 3f);

            // Strong faction presence → negative impulse into war exposure
            var rules = new List<CouplingRule>
            {
                new CouplingRule("faction.presence", "war.exposure")
                {
                    InputChannelSelector  = "*",           // "red" and "blue"
                    OutputChannelSelector = "explicit:[x]", // target war's "x" channel
                    Operator = CouplingOperator.Linear(-0.1f),
                    ScaleByDeltaTime = false,
                }
            };

            CouplingProcessor.Step(dim, rules, 1f);

            // red: -0.1×2 = -0.2, blue: -0.1×1 = -0.1  → total -0.3 into war.x
            Assert.That(warField.GetLogAmp("north", "x"), Is.EqualTo(2.7f).Within(1e-4f)); // 3.0 - 0.3
        }

        [Test]
        public void Integration_MultiTick_WarCouplingConvergesToEquilibrium()
        {
            // Per-tick discrete sequence (injection then decay):
            //   val += -R × war × dt  →  -0.2 × 2.0 × 1.0 = -0.4
            //   val *= (1 - D × dt)   →  val × (1 - 0.4 × 1.0) = val × 0.6
            // Discrete equilibrium: val = (val - 0.4) × 0.6
            //   → 0.4 × val = -0.24  → val_eq = -0.6
            // (Continuous-time formula -R×war/D = -1.0 only holds as dt→0)
            var dim = MakeDimension("north");
            var warField   = dim.AddField("war.exposure", MakeProfile("wp", decay: 0f));
            var availField = dim.AddField("economy.availability",
                new FieldProfile("ep")
                {
                    LogEpsilon     = 0.0001f,
                    DecayRate      = 0.4f,
                    MinLogAmpClamp = -10f,
                    MaxLogAmpClamp =  10f,
                });

            warField.AddLogAmp("north", "x", 2f);     // constant war (no decay)

            var rules = new List<CouplingRule>
            {
                new CouplingRule("war.exposure", "economy.availability")
                {
                    InputChannelSelector  = "x",
                    OutputChannelSelector = "explicit:[ore]",
                    Operator = CouplingOperator.Linear(-0.2f),
                    ScaleByDeltaTime = true,
                }
            };

            const float dt = 1f;
            for (int i = 0; i < 120; i++)
            {
                CouplingProcessor.Step(dim, rules, dt);
                // Simulate economy decay (normally done by Propagator)
                float cur = availField.GetLogAmp("north", "ore");
                availField.SetLogAmp("north", "ore", cur * (1f - 0.4f * dt));
            }

            float final = availField.GetLogAmp("north", "ore");
            Assert.That(final, Is.LessThan(-0.50f));
            Assert.That(final, Is.GreaterThan(-0.70f));
        }
    }
}
