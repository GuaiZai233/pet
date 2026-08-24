using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using GuaiMiao.Animation;
using GuaiMiao.Infrastructure;
using GuaiMiao.Models;
using GuaiMiao.Services;

namespace GuaiMiao.Diagnostics;

internal static class SelfTest
{
    private static readonly Dictionary<string, int> ExpectedFrames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = 6,
        ["running-right"] = 8,
        ["running-left"] = 8,
        ["waving"] = 4,
        ["jumping"] = 5,
        ["failed"] = 8,
        ["waiting"] = 6,
        ["running"] = 6,
        ["review"] = 6,
        ["paw-glass"] = 8
    };

    public static int Run(string reportPath)
    {
        var checks = new Dictionary<string, object?>();
        var errors = new List<string>();
        try
        {
            checks["windows"] = OperatingSystem.IsWindows();
            checks["x64Process"] = RuntimeInformation.ProcessArchitecture == Architecture.X64;
            var catalog = AnimationCatalog.Load();
            checks["cellSize"] = new[] { catalog.CellWidth, catalog.CellHeight };
            if (catalog.CellWidth != 192 || catalog.CellHeight != 208)
                errors.Add("动画单元格不是 192x208。 ");

            var sprites = new SpriteLibrary(catalog);
            foreach (var expected in ExpectedFrames)
            {
                var frames = sprites.GetFrames(expected.Key);
                if (frames.Count != expected.Value)
                {
                    errors.Add($"{expected.Key} 帧数错误：{frames.Count}。");
                    continue;
                }
                for (var index = 0; index < frames.Count; index++)
                    if (!HasVisiblePixels(frames[index]))
                        errors.Add($"{expected.Key}/{index} 是空帧。");
            }

            var atlasHash = HashEmbeddedResource("GuaiMiao.Assets.codex-spritesheet.webp");
            checks["codexAtlasSha256"] = atlasHash;
            if (!atlasHash.Equals(AppInfo.CodexAtlasSha256, StringComparison.OrdinalIgnoreCase))
                errors.Add("内嵌 Codex 图集哈希不匹配。");
            checks["animationStates"] = ExpectedFrames.Count;
            var hoverDurations = catalog.Get("jumping").DurationsMs;
            checks["hoverAnimation"] = "jumping x3, then idle while pointer remains";
            checks["hoverDurationsMs"] = hoverDurations;
            if (!hoverDurations.SequenceEqual(new[] { 140, 140, 140, 140, 280 }))
                errors.Add("悬停原地扑动画节奏未与 Codex 对齐。");
            if (PetInteractionPolicy.HoverPounceLoops != 3)
                errors.Add("悬停原地扑没有连续播放三次。");

            var hoverGate = new HoverInteractionGate();
            var firstHover = hoverGate.TryEnter();
            var duplicateHoverRejected = !hoverGate.TryEnter();
            var insideDoesNotEnd = hoverGate.ObservePointer(true) == HoverExitResult.None;
            var firstOutsideDoesNotEnd = hoverGate.ObservePointer(false) == HoverExitResult.None;
            var insideResetsExitDebounce = hoverGate.ObservePointer(true) == HoverExitResult.None;
            var exitResult = HoverExitResult.None;
            for (var sample = 0; sample < PetInteractionPolicy.HoverExitSamples; sample++)
                exitResult = hoverGate.ObservePointer(false);
            var exitsAfterDebounce = exitResult == HoverExitResult.ActiveHoverEnded;
            checks["hoverDebounce"] = new
            {
                firstHover,
                duplicateHoverRejected,
                insideDoesNotEnd,
                firstOutsideDoesNotEnd,
                insideResetsExitDebounce,
                exitsAfterDebounce,
                samples = PetInteractionPolicy.HoverExitSamples,
                margin = PetInteractionPolicy.HoverExitMargin
            };
            if (!firstHover || !duplicateHoverRejected || !insideDoesNotEnd || !firstOutsideDoesNotEnd ||
                !insideResetsExitDebounce || !exitsAfterDebounce)
                errors.Add("悬停进入锁和离开防抖未生效。");

            hoverGate.SuppressUntilExit();
            var suppressedEnterRejected = !hoverGate.TryEnter();
            for (var sample = 0; sample < PetInteractionPolicy.HoverExitSamples; sample++)
                exitResult = hoverGate.ObservePointer(false);
            var suppressionCleared = exitResult == HoverExitResult.SuppressionCleared && hoverGate.TryEnter();
            checks["postDragHoverSuppression"] = new { suppressedEnterRejected, suppressionCleared };
            if (!suppressedEnterRejected || !suppressionCleared)
                errors.Add("拖动后的悬停抑制未等到指针真正离开。");

            var dragDirection = new DragDirectionTracker();
            var smallRightIgnored = dragDirection.Observe(0.75) == 0 && dragDirection.Observe(0.75) == 0;
            var rightDetected = dragDirection.Observe(0.75) == 1;
            var reverseJitterIgnored = dragDirection.Observe(-0.75) == 1 && dragDirection.Observe(-0.75) == 1;
            var leftDetected = dragDirection.Observe(-0.75) == -1;
            checks["dragDirectionDebounce"] = new
            {
                smallRightIgnored,
                rightDetected,
                reverseJitterIgnored,
                leftDetected,
                threshold = PetInteractionPolicy.DragDirectionThreshold
            };
            if (!smallRightIgnored || !rightDetected || !reverseJitterIgnored || !leftDetected)
                errors.Add("拖动方向防抖或左右步态切换不正确。");
            checks["automaticDwellSeconds"] = new[]
            {
                PetInteractionPolicy.AutomaticDelayMinSeconds,
                PetInteractionPolicy.AutomaticDelayMaxExclusiveSeconds - 1
            };
            if (PetInteractionPolicy.AutomaticDelayMinSeconds < 45 ||
                PetInteractionPolicy.AutomaticActionLoops < 2)
                errors.Add("自动造型停留或间隔仍然过短。");

            var testSuffix = $"self-test-{Environment.ProcessId}";
            var testMutexName = $@"Local\GuaiMiao-{testSuffix}";
            var testPipeName = $"GuaiMiao-{testSuffix}";
            using (var primary = SingleInstanceService.TryAcquire(testMutexName, testPipeName))
            {
                checks["singleInstancePrimary"] = primary is not null;
                if (primary is null)
                {
                    errors.Add("无法取得单实例互斥锁；可能已有乖喵正在运行。");
                }
                else
                {
                    using var duplicate = SingleInstanceService.TryAcquire(testMutexName, testPipeName);
                    checks["singleInstanceDuplicateRejected"] = duplicate is null;
                    if (duplicate is not null)
                        errors.Add("单实例互斥锁未拒绝第二个实例。");

                    using var received = new ManualResetEventSlim(false);
                    var command = string.Empty;
                    primary.CommandReceived += value =>
                    {
                        command = value;
                        received.Set();
                    };
                    primary.StartServer();
                    var sent = SingleInstanceService.SendAsync("self-test", 2000, testPipeName)
                        .GetAwaiter().GetResult();
                    var roundTrip = sent && received.Wait(2000) && command == "self-test";
                    checks["singleInstancePipeRoundTrip"] = roundTrip;
                    if (!roundTrip)
                        errors.Add("单实例命名管道回环测试失败。");
                }
            }

            checks["aboutText"] = AppInfo.AboutText;
            checks["homepage"] = AppInfo.HomepageUrl;
            checks["latestReleaseApi"] = AppInfo.LatestReleaseApiUrl;
            checks["autostartCommand"] = AutostartService.ExpectedCommand;
            if (!AutostartService.ExpectedCommand.Contains(AppInfo.InstalledArgument, StringComparison.Ordinal) ||
                !AutostartService.ExpectedCommand.Contains(AppPaths.InstalledExecutable, StringComparison.OrdinalIgnoreCase))
                errors.Add("开机启动命令不完整。");
            var defaultSettings = new AppSettings();
            checks["settingsSchema"] = defaultSettings.SchemaVersion;
            checks["autoRunDefault"] = defaultSettings.AutoRunEnabled;
            if (defaultSettings.SchemaVersion != 3 || !defaultSettings.AutoRunEnabled)
                errors.Add("自动跑动的默认设置或设置版本错误。");

            var version = new Version(1, 2, 0, 0);
            var sameVersionHotfix = SelfInstaller.ShouldUpgrade(version, version, sameBinary: false);
            var identicalBinarySkipped = !SelfInstaller.ShouldUpgrade(version, version, sameBinary: true);
            var downgradeSkipped = !SelfInstaller.ShouldUpgrade(new Version(1, 1, 0, 0), version,
                sameBinary: false);
            checks["sameVersionHotfix"] = new
            {
                sameVersionHotfix,
                identicalBinarySkipped,
                downgradeSkipped
            };
            if (!sameVersionHotfix || !identicalBinarySkipped || !downgradeSkipped)
                errors.Add("同版本热修复升级判定不正确。");
        }
        catch (Exception ex)
        {
            errors.Add($"{ex.GetType().Name}: {ex.Message}");
        }

        var report = new
        {
            ok = errors.Count == 0,
            product = AppInfo.ProductName,
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
            os = RuntimeInformation.OSDescription,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            checks,
            errors,
            testedAt = DateTimeOffset.Now
        };
        var fullPath = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        return errors.Count == 0 ? 0 : 1;
    }

    private static bool HasVisiblePixels(BitmapSource frame)
    {
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] > 0)
                return true;
        return false;
    }

    private static string HashEmbeddedResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"缺少资源：{name}");
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
