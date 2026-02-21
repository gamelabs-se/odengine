using System;
using NUnit.Framework;
using Odengine.Fields;

namespace Odengine.Tests.Core.Fields
{
    [TestFixture]
    public class Fields_ChannelViewTests
    {
        private static FieldProfile DefaultProfile() =>
            new FieldProfile("test.field") { PropagationRate = 1f, EdgeResistanceScale = 1f, DecayRate = 0f };

        // ── Delegation ──────────────────────────────────────────────────────

        [Test]
        public void GetLogAmp_DelegatesToField()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 2.5f);
            var view = field.ForChannel("ch");
            Assert.AreEqual(field.GetLogAmp("n", "ch"), view.GetLogAmp("n"), 1e-9f);
        }

        [Test]
        public void GetMultiplier_DelegatesToField()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch", 1f);
            var view = field.ForChannel("ch");
            Assert.AreEqual(field.GetMultiplier("n", "ch"), view.GetMultiplier("n"), 1e-6f);
        }

        [Test]
        public void AddLogAmp_WritesToField()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            view.AddLogAmp("n", 3f);
            Assert.AreEqual(3f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        [Test]
        public void SetLogAmp_WritesToField()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            view.SetLogAmp("n", 7f);
            Assert.AreEqual(7f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        // ── State sharing ────────────────────────────────────────────────────

        [Test]
        public void MultipleViewsSameChannel_ShareState()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view1 = field.ForChannel("ch");
            var view2 = field.ForChannel("ch");

            view1.AddLogAmp("n", 1f);
            Assert.AreEqual(1f, view2.GetLogAmp("n"), 1e-6f);
        }

        [Test]
        public void ChangeThroughField_ReflectedInView()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            field.SetLogAmp("n", "ch", 4f);
            Assert.AreEqual(4f, view.GetLogAmp("n"), 1e-6f);
        }

        [Test]
        public void ChangeThroughView_ReflectedInField()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            view.SetLogAmp("n", 5f);
            Assert.AreEqual(5f, field.GetLogAmp("n", "ch"), 1e-6f);
        }

        // ── Channel isolation ────────────────────────────────────────────────

        [Test]
        public void DifferentChannelViews_DontInterfere()
        {
            var field = new ScalarField("f", DefaultProfile());
            var viewA = field.ForChannel("ch-a");
            var viewB = field.ForChannel("ch-b");

            viewA.AddLogAmp("n", 2f);
            Assert.AreEqual(0f, viewB.GetLogAmp("n"), 1e-9f,
                "Write to ch-a must not affect ch-b");
        }

        [Test]
        public void ChannelView_ReadsOwnChannelOnly()
        {
            var field = new ScalarField("f", DefaultProfile());
            field.SetLogAmp("n", "ch-a", 3f);
            field.SetLogAmp("n", "ch-b", 7f);

            var viewA = field.ForChannel("ch-a");
            var viewB = field.ForChannel("ch-b");

            Assert.AreEqual(3f, viewA.GetLogAmp("n"), 1e-6f);
            Assert.AreEqual(7f, viewB.GetLogAmp("n"), 1e-6f);
        }

        // ── Neutral baseline via view ────────────────────────────────────────

        [Test]
        public void NeutralBaseline_ViewOnUnsetChannel_ReturnsZeroLogAmp()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ghost");
            Assert.AreEqual(0f, view.GetLogAmp("n"), 1e-9f);
        }

        [Test]
        public void NeutralBaseline_ViewOnUnsetChannel_ReturnsOneMultiplier()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ghost");
            Assert.AreEqual(1f, view.GetMultiplier("n"), 1e-6f);
        }

        // ── Add / set accumulation via view ──────────────────────────────────

        [Test]
        public void AddLogAmp_Accumulates_ViaView()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            view.AddLogAmp("n", 1f);
            view.AddLogAmp("n", 2f);
            Assert.AreEqual(3f, view.GetLogAmp("n"), 1e-6f);
        }

        [Test]
        public void SetLogAmp_Overwrites_ViaView()
        {
            var field = new ScalarField("f", DefaultProfile());
            var view = field.ForChannel("ch");
            view.SetLogAmp("n", 1f);
            view.SetLogAmp("n", 99f);
            Assert.AreEqual(99f, view.GetLogAmp("n"), 1e-6f);
        }

        // ── Case sensitivity ──────────────────────────────────────────────────

        [Test]
        public void ChannelView_CaseSensitive_DifferentViewsDontShare()
        {
            var field = new ScalarField("f", DefaultProfile());
            var viewLower = field.ForChannel("ch");
            var viewUpper = field.ForChannel("CH");

            viewLower.SetLogAmp("n", 1f);

            Assert.AreEqual(1f, viewLower.GetLogAmp("n"), 1e-6f);
            Assert.AreEqual(0f, viewUpper.GetLogAmp("n"), 1e-9f,
                "Channel IDs must be case-sensitive");
        }
    }
}
