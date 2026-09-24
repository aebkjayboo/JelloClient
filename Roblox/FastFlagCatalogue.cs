namespace JelloClient.Roblox;

internal sealed record FlagValue(string Flag, string Value);

internal sealed record FlagPreset(string Id, string Group, string Name, string Description, IReadOnlyList<FlagValue> Flags);

/// Preset definitions resolved from Voidstrap's FastFlagManager preset table and
/// its FastFlags view model. Every flag name and value here came from that source;
/// presets whose setter could not be resolved were left out rather than guessed.
internal static class FastFlagCatalogue
{
    public static readonly IReadOnlyDictionary<string, string> GroupDescriptions =
        new Dictionary<string, string>
        {
            ["Rendering"] = "Visual fidelity traded for framerate.",
            ["Performance"] = "Engine work scheduling and asset loading.",
            ["Network"] = "Replication and packet behaviour.",
            ["Cache"] = "How much Roblox keeps on disk.",
            ["Telemetry"] = "What Roblox reports back.",
            ["Interface"] = "Client UI behaviour.",
        };

    public static readonly IReadOnlyList<FlagPreset> Presets = new FlagPreset[]
    {
        new("DisablePostFX", "Rendering", "Disable post-processing",
            "Turns off bloom, blur and colour grading effects.",
            new FlagValue[]
            {
                new("FFlagDisablePostFx", "True"),
            }),

        new("DisableSky", "Rendering", "Disable low-quality bloom",
            "Turns off the low-framerate bloom pass and the FRM refactor path.",
            new FlagValue[]
            {
                new("FFlagRenderNoLowFrmBloom", "False"),
                new("FFlagFRMRefactor", "False"),
            }),

        new("DisablePlayerShadows", "Rendering", "Disable player shadows",
            "Removes character shadows and pauses the shadow voxelizer.",
            new FlagValue[]
            {
                new("FIntRenderShadowIntensity", "0"),
                new("DFFlagDebugPauseVoxelizer", "True"),
                new("FIntRenderShadowmapBias", "-1"),
            }),

        new("GrayAvatar", "Rendering", "Untextured avatars",
            "Stops the texture compositor, so avatars render untextured.",
            new FlagValue[]
            {
                new("DFIntTextureCompositorActiveJobs", "0"),
            }),

        new("LowPolyMeshes", "Rendering", "Low-poly meshes",
            "Forces the lowest mesh detail level at every distance.",
            new FlagValue[]
            {
                new("DFIntCSGLevelOfDetailSwitchingDistance", "0"),
                new("DFIntCSGLevelOfDetailSwitchingDistanceL12", "0"),
                new("DFIntCSGLevelOfDetailSwitchingDistanceL23", "0"),
                new("DFIntCSGLevelOfDetailSwitchingDistanceL34", "0"),
            }),

        new("DisableTerrainTextures", "Rendering", "Disable terrain textures",
            "Renders terrain with flat colours instead of textures.",
            new FlagValue[]
            {
                new("FIntTerrainArraySliceSize", "0"),
            }),

        new("MinimalRendering", "Rendering", "Minimal rendering",
            "Deterministic rendering path that skips a lot of visual work.",
            new FlagValue[]
            {
                new("FFlagDebugRenderingSetDeterministic", "True"),
            }),

        new("RenderOcclusion", "Rendering", "Occlusion culling",
            "Skips drawing geometry hidden behind other geometry.",
            new FlagValue[]
            {
                new("DFFlagUseVisBugChecks", "True"),
                new("FFlagEnableVisBugChecks27", "True"),
                new("FFlagVisBugChecksThreadYield", "True"),
            }),

        new("OptimizeCFrameUpdates", "Performance", "Optimise CFrame updates",
            "Batches transform updates. Usually a straight win.",
            new FlagValue[]
            {
                new("FFlagOptimizeCFrameUpdates4", "True"),
                new("FFlagOptimizeCFrameUpdatesIC4", "True"),
            }),

        new("LessLagSpikes", "Performance", "Reduce lag spikes",
            "Raises the bandwidth ceiling and shortens the sender catch-up window.",
            new FlagValue[]
            {
                new("DFIntBandwidthManagerApplicationDefaultBps", "796850000"),
                new("DFIntBandwidthManagerDataSenderMaxWorkCatchupMs", "5"),
            }),

        new("FasterLoading", "Performance", "Faster asset loading",
            "Raises the preload cap and shortens the player-image timeout.",
            new FlagValue[]
            {
                new("DFIntNumAssetsMaxToPreload", "2147483647"),
                new("FStringGetPlayerImageDefaultTimeout", "1"),
                new("DFFlagEnableMeshPreloading2", "True"),
            }),

        new("Preload", "Performance", "Preload assets",
            "Preloads meshes, sounds and textures ahead of use.",
            new FlagValue[]
            {
                new("DFFlagEnableMeshPreloading2", "True"),
                new("DFFlagEnableSoundPreloading", "True"),
                new("DFFlagEnableTexturePreloading", "True"),
                new("DFFlagTeleportClientAssetPreloadingEnabled9", "True"),
                new("FFlagPreloadAllFonts", "True"),
                new("FFlagPreloadTextureItemsOption4", "True"),
                new("DFFlagTeleportPreloadingMetrics5", "True"),
            }),

        new("Prerender", "Performance", "Move prerender",
            "Moves the prerender step off the critical path.",
            new FlagValue[]
            {
                new("FFlagMovePrerender", "True"),
                new("FFlagMovePrerenderV2", "True"),
            }),

        new("MemoryProbing", "Performance", "Memory probing",
            "Enables the engine performance-control memory prober.",
            new FlagValue[]
            {
                new("DFFlagPerformanceControlEnableMemoryProbing3", "True"),
            }),

        new("BetterPacketSending", "Network", "Faster packet sending",
            "Cuts artificial packet delays so input reaches the server sooner.",
            new FlagValue[]
            {
                new("DFIntNetworkStopProducingPacketsToProcessThresholdMs", "0"),
                new("DFIntMaxWaitTimeBeforeForcePacketProcessMS", "1"),
                new("DFIntClientPacketMaxDelayMs", "1"),
                new("DFIntClientPacketMinMicroseconds", "1"),
                new("DFIntClientPacketExcessMicroseconds", "1"),
                new("DFIntClientPacketMaxFrameMicroseconds", "1047483647"),
                new("DFIntMaxProcessPacketsJobScaling", "5000000"),
                new("DFIntMaxProcessPacketsStepsAccumulated", "1"),
                new("DFIntMaxProcessPacketsStepsPerCyclic", "1047483647"),
            }),

        new("NoPayloadLimit", "Network", "Remove payload limits",
            "Raises every replication payload cap to its maximum.",
            new FlagValue[]
            {
                new("DFIntRccMaxPayloadSnd", "2147483647"),
                new("DFIntCliMaxPayloadRcv", "2147483647"),
                new("DFIntCliMaxPayloadSnd", "2147483647"),
                new("DFIntRccMaxPayloadRcv", "2147483647"),
                new("DFIntCliTcMaxPayloadRcv", "2147483647"),
                new("DFIntRccTcMaxPayloadRcv", "2147483647"),
                new("DFIntCliTcMaxPayloadSnd", "2147483647"),
                new("DFIntRccTcMaxPayloadSnd", "2147483647"),
                new("DFIntMaxDataPayloadSize", "2147483647"),
                new("DFIntMaxUREPayloadSingleLimit", "2147483647"),
                new("DFIntTotalRepPayloadLimit", "2147483647"),
            }),

        new("EnableLargeReplicator", "Network", "Large replicator",
            "Turns on the newer large-replicator read and write paths.",
            new FlagValue[]
            {
                new("FFlagLargeReplicatorEnabled7", "True"),
                new("FFlagLargeReplicatorWrite5", "True"),
                new("FFlagLargeReplicatorRead5", "True"),
                new("FFlagLargeReplicatorSerializeRead3", "True"),
                new("FFlagLargeReplicatorSerializeWrite3", "True"),
            }),

        new("CacheSizeImprovement", "Cache", "Larger asset cache",
            "Raises Roblox cache sizes and lifetimes so less is refetched.",
            new FlagValue[]
            {
                new("FFlagClearCacheableContentProviderOnGameLaunch", "True"),
                new("DFFlagAlwaysSkipDiskCache", "False"),
                new("FFlagUseCachedAudibilityMeasurements", "True"),
                new("DFIntCachedPatchLoadDelayMilliseconds", "1"),
                new("DFIntHttpCacheCleanScheduleAfterMs", "1036372536"),
                new("DFIntHttpCacheCleanUpToAvailableSpaceMiB", "1036372536"),
                new("DFIntHttpCacheAsyncWriterMaxPendingSize", "1036372536"),
                new("DFIntHttpCacheEvictionExemptionMapMaxSize", "1036372536"),
                new("DFIntHttpCacheReportSlowWritesMinDuration", "1036372536"),
                new("DFIntMemCacheMaxCapacityMB", "1036372536"),
                new("DFIntFileCacheReserveSize", "1036372536"),
                new("DFIntThirdPartyInMemoryCacheCapacity", "1036372536"),
                new("DFIntSoundServiceCacheCleanupMaxAgeDays", "1036372536"),
                new("DFIntUserIdPlayerNameCacheLifetimeSeconds", "1036372536"),
                new("DFIntAssetCacheErrorLogHundredthsPercent", "2147483647"),
                new("DFFlagHttpTrackSyncWriteCachePhase", "True"),
                new("DFIntHttpCachePerfSamplingRate", "1036372536"),
                new("DFIntHttpCachePerfHundredthsPercent", "1036372536"),
                new("DFIntReportCacheDirSizesHundredthsPercent", "1036372536"),
            }),

        new("DisableTelemetry", "Telemetry", "Disable telemetry",
            "Points the telemetry endpoint at 0.0.0.0 and disables the reporters.",
            new FlagValue[]
            {
                new("DFStringTelemetryV2Url", "0.0.0.0"),
                new("FFlagEnableTelemetryProtocol", "False"),
                new("DFFlagGraphicsQualityUsageTelemetry", "False"),
                new("DFFlagGpuVsCpuBoundTelemetry", "False"),
                new("DFFlagSendRenderFidelityTelemetry", "False"),
                new("DFFlagReportRenderDistanceTelemetry", "False"),
                new("DFFlagCollectAudioPluginTelemetry", "False"),
                new("DFFlagEnableFmodErrorsTelemetry", "False"),
                new("DFFlagRccLoadSoundLengthTelemetryEnabled", "False"),
                new("DFFlagReportAssetRequestV1Telemetry", "False"),
                new("DFFlagRobloxTelemetryAddDeviceRAMPointsV2", "False"),
                new("DFFlagEnableTelemetryV2FRMStats", "False"),
                new("DFFlagEnableSkipUpdatingGlobalTelemetryInfo2", "False"),
                new("DFFlagEmitSafetyTelemetryInCallbackEnable", "False"),
                new("DFFlagRobloxTelemetryV2PointEncoding", "False"),
                new("DFFlagDSTelemetryV2ReplaceSeparator", "False"),
                new("FFlagOpenTelemetryEnabled", "False"),
                new("FLogRobloxTelemetry", "0"),
                new("FFlagEnableTelemetryService1", "False"),
                new("FFlagPropertiesEnableTelemetry", "False"),
            }),

        new("DisableVoiceChatTelemetry", "Telemetry", "Disable voice telemetry",
            "Turns off voice-chat analytics reporting. Does not disable voice chat itself.",
            new FlagValue[]
            {
                new("DFFlagVoiceChatCullingRecordEventIngestTelemetry", "False"),
                new("DFFlagVoiceChatJoinProfilingUsingTelemetryStat_RCC", "False"),
                new("DFFlagVoiceChatPossibleDuplicateSubscriptionsTelemetry", "False"),
                new("DFIntVoiceChatTaskStatsTelemetryThrottleHundrethsPercent", "0"),
                new("FFlagEnableLuaVoiceChatAnalyticsV2", "False"),
                new("FFlagLuaVoiceChatAnalyticsBanMessage", "False"),
                new("FFlagLuaVoiceChatAnalyticsUseCounterV2", "False"),
                new("FFlagLuaVoiceChatAnalyticsUseEventsV2", "False"),
                new("FFlagLuaVoiceChatAnalyticsUsePointsV2", "False"),
                new("FFlagVoiceChatCullingEnableMutedSubsTelemetry", "False"),
                new("FFlagVoiceChatCullingEnableStaleSubsTelemetry", "False"),
                new("FFlagVoiceChatCustomAudioDeviceEnableNeedMorePlayoutTelemetry", "False"),
                new("FFlagVoiceChatCustomAudioDeviceEnableNeedMorePlayoutTelemetry3", "False"),
                new("FFlagVoiceChatCustomAudioMixerEnableUpdateSourcesTelemetry2", "False"),
                new("FFlagVoiceChatDontSendTelemetryForPubIceTrickle", "False"),
                new("FFlagVoiceChatPeerConnectionTelemetryDetails", "False"),
                new("FFlagVoiceChatRobloxAudioDeviceUpdateRecordedBufferTelemetryEnabled", "False"),
                new("FFlagVoiceChatSubscriptionsDroppedTelemetry", "False"),
                new("FIntLuaVoiceChatAnalyticsPointsThrottle", "0"),
                new("FIntVoiceChatPerfSensitiveTelemetryIntervalSeconds", "-1"),
            }),

        new("DisableWebview2Telemetry", "Telemetry", "Disable WebView telemetry",
            "Turns off telemetry from the in-client web views.",
            new FlagValue[]
            {
                new("DFStringWebviewUrlAllowlist", "www.youtube-nocookie.com"),
                new("DFFlagWindowsWebViewTelemetryEnabled", "False"),
                new("DFIntMacWebViewTelemetryThrottleHundredthsPercent", "0"),
                new("DFIntWindowsWebViewTelemetryThrottleHundredthsPercent", "0"),
                new("FIntStudioWebView2TelemetryHundredthsPercent", "0"),
                new("FFlagSyncWebViewCookieToEngine2", "False"),
                new("FFlagUpdateHTTPCookieStorageFromWKWebView", "False"),
            }),

        new("BlockTencent", "Telemetry", "Block Tencent endpoints",
            "Redirects the Tencent auth and China-policy endpoints away.",
            new FlagValue[]
            {
                new("FStringTencentAuthPath", "/tencent/"),
                new("FLogTencentAuthPath", "/tencent/"),
                new("FStringXboxExperienceGuidelinesUrl", "https://www.gov.cn"),
                new("FStringExperienceGuidelinesExplainedPageUrl", "https://www.gov.cn"),
                new("DFFlagPolicyServiceReportIsNotSubjectToChinaPolicies", "False"),
                new("DFFlagPolicyServiceReportDetailIsNotSubjectToChinaPolicies", "False"),
                new("DFIntPolicyServiceReportDetailIsNotSubjectToChinaPoliciesHundredthsPercentage", "10000"),
            }),

        new("DisableAds", "Interface", "Disable ads",
            "Turns off the ad service and the sponsored-tile tooltips.",
            new FlagValue[]
            {
                new("FFlagAdServiceEnabled", "False"),
                new("FFlagEnableSponsoredAdsGameCarouselTooltip3", "False"),
                new("FFlagEnableSponsoredAdsPerTileTooltipExperienceFooter", "False"),
                new("FFlagEnableSponsoredAdsSeeAllGamesListTooltip", "False"),
                new("FFlagEnableSponsoredTooltipForAvatarCatalog2", "False"),
                new("FFlagLuaAppSponsoredGridTiles", "False"),
            }),

        new("NoGuiBlur", "Interface", "No menu blur",
            "Removes the blur behind Roblox UI panels.",
            new FlagValue[]
            {
                new("FIntRobloxGuiBlurIntensity", "0"),
            }),

        new("ChatBubble", "Interface", "Disable bubble chat",
            "Stops bubble chat being driven from the chat service.",
            new FlagValue[]
            {
                new("FFlagEnableBubbleChatFromChatService", "False"),
            }),

        new("FullscreenTitlebarDisabled", "Interface", "Hide fullscreen title bar",
            "Pushes the fullscreen title-bar trigger delay out to an hour.",
            new FlagValue[]
            {
                new("FIntFullscreenTitleBarTriggerDelayMillis", "3600000"),
            }),

        new("DisplayFps", "Interface", "Show FPS counter",
            "Draws the engine framerate counter on screen.",
            new FlagValue[]
            {
                new("FFlagDebugDisplayFPS", "True"),
            }),

        new("FixDisplayScaling", "Interface", "Fix display scaling",
            "Stops Roblox applying Windows DPI scaling, fixing a blurry client.",
            new FlagValue[]
            {
                new("DFFlagDisableDPIScale", "True"),
            }),

        new("Pseudolocalization", "Interface", "Pseudolocalisation",
            "Debug mode that pads and accents UI strings to test layout.",
            new FlagValue[]
            {
                new("FFlagDebugEnablePseudolocalization", "True"),
            }),

    };
}
