using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering.Universal;

namespace Pivot.Tests
{
    /// <summary>
    /// The render settings are tuning knobs that a debug session can reach at runtime,
    /// and the URP Asset is a file under version control. These tests are the tripwire:
    /// if a tuning session ever leaves MSAA at 1x or depth off in the committed asset,
    /// the suite fails instead of the regression shipping quietly.
    ///
    /// They also pin the handful of settings that were reasoned about rather than
    /// defaulted, so that reasoning has to be revisited deliberately.
    /// </summary>
    public class RenderSettingsGuardTests
    {
        const string MobileAsset = "Assets/Settings/Mobile_RPAsset.asset";
        const string PcAsset = "Assets/Settings/PC_RPAsset.asset";
        const string MobileRenderer = "Assets/Settings/Mobile_Renderer.asset";
        const string PcRenderer = "Assets/Settings/PC_Renderer.asset";

        static UniversalRenderPipelineAsset Load(string path)
        {
            UniversalRenderPipelineAsset asset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            Assert.IsNotNull(asset, "missing pipeline asset at " + path);
            return asset;
        }

        static UniversalRendererData LoadRenderer(string path)
        {
            UniversalRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            Assert.IsNotNull(data, "missing renderer data at " + path);
            return data;
        }

        [Test]
        public void BothTiers_KeepFourTimesMsaa()
        {
            Assert.AreEqual(4, Load(MobileAsset).msaaSampleCount,
                "Mobile MSAA must stay at 4x. If a tuning session dropped it, restore it here.");
            Assert.AreEqual(4, Load(PcAsset).msaaSampleCount, "PC MSAA must stay at 4x.");
        }

        [Test]
        public void BothTiers_KeepTheCameraDepthTexture()
        {
            Assert.IsTrue(Load(MobileAsset).supportsCameraDepthTexture,
                "soft particles need the depth texture; turning it off is a deliberate call, not a leftover");
            Assert.IsTrue(Load(PcAsset).supportsCameraDepthTexture);
        }

        [Test]
        public void BothTiers_RenderAtFullScale()
        {
            // 0.8 was the template default and it makes the node numbers mushy.
            // Legibility is the product, so this one is not negotiable without a decision.
            Assert.AreEqual(1f, Load(MobileAsset).renderScale, 0.001f);
            Assert.AreEqual(1f, Load(PcAsset).renderScale, 0.001f);
        }

        [Test]
        public void MobileKeepsHdrOff_SoPassthroughHasAnAlphaChannel()
        {
            Assert.IsFalse(Load(MobileAsset).supportsHDR,
                "HDR on the mobile tier renders to R11G11B10, which has no alpha, " +
                "and passthrough composites on alpha");
        }

        [Test]
        public void PcKeepsHdrOn_BecauseThatIsWhereBloomLives()
        {
            Assert.IsTrue(Load(PcAsset).supportsHDR);
        }

        [Test]
        public void NeitherTierPaysForTheOpaqueTextureCopy()
        {
            Assert.IsFalse(Load(MobileAsset).supportsCameraOpaqueTexture);
            Assert.IsFalse(Load(PcAsset).supportsCameraOpaqueTexture);
        }

        [Test]
        public void NeitherTierPaysForTerrainHoles()
        {
            Assert.IsFalse(Load(MobileAsset).supportsTerrainHoles);
            Assert.IsFalse(Load(PcAsset).supportsTerrainHoles);
        }

        [Test]
        public void BothRenderersStayOnPlainForward()
        {
            // One directional key and one directional fill. Forward+ would pay for
            // light clustering that nothing in this design ever reads.
            Assert.AreEqual(RenderingMode.Forward, LoadRenderer(MobileRenderer).renderingMode);
            Assert.AreEqual(RenderingMode.Forward, LoadRenderer(PcRenderer).renderingMode);
        }

        [Test]
        public void MobileRendererLeavesIntermediateTextureOnAuto()
        {
            // Forcing it is called out in Unity's own Quest guidance as a significant
            // cost on that hardware.
            Assert.AreEqual(IntermediateTextureMode.Auto,
                LoadRenderer(MobileRenderer).intermediateTextureMode);
        }

        [Test]
        public void AndroidBuildsVulkanOnly()
        {
            UnityEngine.Rendering.GraphicsDeviceType[] apis =
                PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);

            Assert.AreEqual(1, apis.Length,
                "GLES3 back in the list reintroduces the depth-copy trap: CanCopyDepth " +
                "returns false on GLES with MSAA, which forces a full depth prepass");
            Assert.AreEqual(UnityEngine.Rendering.GraphicsDeviceType.Vulkan, apis[0]);
            Assert.IsFalse(PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android));
        }
    }
}
