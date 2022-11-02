using Stride.Graphics;
using Stride.Rendering.Lights;
using System.Collections.Generic;
using TR.Stride.Atmosphere;
using VL.Core;
using VL.Stride.Rendering.Lights;

namespace VL.Stride.Rendering.Compositing
{
    static class AtmosphereNodes
    {
        public static IEnumerable<IVLNodeDescription> GetNodeDescriptions(IVLNodeDescriptionFactory factory)
        {
            var renderingCategory = "Stride.Rendering";
            var renderingCategoryAdvanced = $"{renderingCategory}.Advanced";

            // Sub render features for mesh render feature
            var renderFeaturesCategory = $"{renderingCategoryAdvanced}.RenderFeatures";

            // Light renderers - make enum
            var lightsCategory = $"{renderingCategoryAdvanced}.Light";

            var lightComponentCategory = "Stride.Lights.Advanced";
            yield return LightNodes.NewDirectLightNode<AtmosphereLightDirectional>(factory, lightComponentCategory)
                .AddCachedInput(nameof(AtmosphereLightDirectional.Atmosphere), x => x.Atmosphere, (x, v) => x.Atmosphere = v, new AtmosphereComponent());

            yield return factory.NewComponentNode<AtmosphereComponent>(lightComponentCategory);

            yield return factory.NewNode<AtmosphereRenderFeature>(category: renderingCategoryAdvanced)
                // Debug
                .AddInput(nameof(AtmosphereRenderFeature.DrawDebugTextures), x => x.DrawDebugTextures, (x, v) => x.DrawDebugTextures = v)
                // Performance
                .AddInput(nameof(AtmosphereRenderFeature.FastSky), x => x.FastSky, (x, v) => x.FastSky = v)
                .AddInput(nameof(AtmosphereRenderFeature.FastAerialPerspectiveEnabled), x => x.FastAerialPerspectiveEnabled, (x, v) => x.FastAerialPerspectiveEnabled = v)
                // Render feature
                .AddCachedListInput(nameof(AtmosphereRenderFeature.RenderStageSelectors), x => x.RenderStageSelectors)
                .AddCachedListInput(nameof(AtmosphereRenderFeature.PipelineProcessors), x => x.PipelineProcessors)
                // Texture settings
                .AddCachedInput(nameof(AtmosphereRenderFeature.AtmosphereCameraScatteringVolumeSettings), x => x.AtmosphereCameraScatteringVolumeSettings, (x, v) => x.AtmosphereCameraScatteringVolumeSettings = v)
                .AddCachedInput(nameof(AtmosphereRenderFeature.MultiScatteringTextureSettings), x => x.MultiScatteringTextureSettings, (x, v) => x.MultiScatteringTextureSettings = v)
                .AddCachedInput(nameof(AtmosphereRenderFeature.SkyViewLutSettings), x => x.SkyViewLutSettings, (x, v) => x.SkyViewLutSettings = v)
                .AddCachedInput(nameof(AtmosphereRenderFeature.TransmittanceLutSettings), x => x.TransmittanceLutSettings, (x, v) => x.TransmittanceLutSettings = v)
                ;

            yield return factory.NewStructNode(renderingCategoryAdvanced, new TextureSettings2d(64, 64, PixelFormat.R16G16B16A16_Float))
               .AddCachedInput(nameof(TextureSettings2d.Width), x => x.v.Width, (x, v) => x.v.Width = v, 64)
               .AddCachedInput(nameof(TextureSettings2d.Width), x => x.v.Height, (x, v) => x.v.Height = v, 64)
               .AddCachedInput(nameof(TextureSettings2d.Format), x => x.v.Format, (x, v) => x.v.Format = v, PixelFormat.R16G16B16A16_Float)
               .AddStateOutput();

            yield return factory.NewStructNode(renderingCategoryAdvanced, new TextureSettingsSquare(64, PixelFormat.R16G16B16A16_Float))
               .AddCachedInput(nameof(TextureSettingsSquare.Size), x => x.v.Size, (x, v) => x.v.Size = v, 64)
               .AddCachedInput(nameof(TextureSettingsSquare.Format), x => x.v.Format, (x, v) => x.v.Format = v, PixelFormat.R16G16B16A16_Float)
               .AddStateOutput();

            yield return factory.NewStructNode(renderingCategoryAdvanced, new TextureSettingsVolume(32, 32, PixelFormat.R16G16B16A16_Float))
                .AddCachedInput(nameof(TextureSettingsVolume.Size), x => x.v.Size, (x, v) => x.v.Size = v, 32)
                .AddCachedInput(nameof(TextureSettingsVolume.Slices), x => x.v.Slices, (x, v) => x.v.Slices = v, 32)
                .AddCachedInput(nameof(TextureSettingsVolume.Format), x => x.v.Format, (x, v) => x.v.Format = v, PixelFormat.R16G16B16A16_Float)
                .AddStateOutput();

            yield return new StrideNodeDesc<AtmosphereTransparentRenderFeature>(factory, category: renderFeaturesCategory);
            yield return new StrideNodeDesc<AtmosphereLightDirectionalGroupRenderer>(factory, category: lightsCategory);

        }

        static CustomNodeDesc<StructRef<T>> NewStructNode<T>(this IVLNodeDescriptionFactory factory, string category, T initial, string name = default) where T : struct
        {
            return factory.NewNode(name: name ?? typeof(T).Name, category: category, copyOnWrite: false, hasStateOutput: false, ctor: _ => S(initial));
        }

        static CustomNodeDesc<StructRef<T>> AddStateOutput<T>(this CustomNodeDesc<StructRef<T>> node) where T : struct
        {
            return node.AddOutput("Output", x => x.v);
        }

        static StructRef<T> S<T>(T value) where T : struct => new StructRef<T>(value);

        class StructRef<T> where T : struct
        {
            public T v;

            public StructRef(T value)
            {
                v = value;
            }
        }
    }
}
