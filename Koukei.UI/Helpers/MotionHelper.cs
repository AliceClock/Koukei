using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.ViewManagement;

namespace Koukei.UI.Helpers;

internal enum MotionPreset
{
    Fast,
    Standard,
    Panel
}

internal enum MotionDirection
{
    None,
    Up,
    Down,
    Left,
    Right
}

internal enum MotionIntent
{
    Interactive,
    PlaybackFollow,
    InitialPosition,
    FocusRestore
}

internal static class MotionHelper
{
    private sealed class ElementMotionState
    {
        public required int Version { get; init; }

        public required CancellationTokenSource Cancellation { get; init; }

        public required double TargetOpacity { get; init; }

        public required Vector3? TargetTranslation { get; init; }

        public required Visibility? TargetVisibility { get; init; }

        public required bool? TargetHitTestVisible { get; init; }

        public Action? PendingMutation;
    }

    private static readonly UISettings UiSettings = new();
    private static readonly object MotionGate = new();
    private static readonly Dictionary<UIElement, ElementMotionState> ActiveMotions = [];
    private static readonly Dictionary<ListViewBase, int> ActiveListPositionRequests = [];
    private static int _nextMotionVersion;
    private static int _nextListPositionVersion;

    static MotionHelper()
    {
        // Some Windows configurations expose UISettings successfully but fail while
        // subscribing to its WinRT event. Motion must never be a startup requirement.
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                UiSettings.AnimationsEnabledChanged += UiSettings_AnimationsEnabledChanged;
            }
        }
        catch (COMException)
        {
        }
        catch
        {
        }
    }

    public static bool AnimationsEnabled
    {
        get
        {
            try
            {
                return UiSettings.AnimationsEnabled;
            }
            catch
            {
                return true;
            }
        }
    }

    public static TimeSpan GetDuration(MotionPreset preset, bool isExit = false)
    {
        var fallbackMilliseconds = preset switch
        {
            MotionPreset.Fast => 120d,
            MotionPreset.Standard => 180d,
            MotionPreset.Panel when isExit => 120d,
            MotionPreset.Panel => 220d,
            _ => 180d
        };

        var resourceKey = preset switch
        {
            MotionPreset.Fast => "KoukeiMotionFastMilliseconds",
            MotionPreset.Standard => "KoukeiMotionStandardMilliseconds",
            MotionPreset.Panel when isExit => "KoukeiMotionPanelExitMilliseconds",
            MotionPreset.Panel => "KoukeiMotionPanelMilliseconds",
            _ => "KoukeiMotionStandardMilliseconds"
        };

        try
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
                value is double milliseconds)
            {
                return TimeSpan.FromMilliseconds(milliseconds);
            }
        }
        catch
        {
        }

        return TimeSpan.FromMilliseconds(fallbackMilliseconds);
    }

    public static double GetDistance(MotionPreset preset)
    {
        var fallbackDistance = preset switch
        {
            MotionPreset.Fast => 8d,
            MotionPreset.Standard => 12d,
            MotionPreset.Panel => 16d,
            _ => 8d
        };

        var resourceKey = preset switch
        {
            MotionPreset.Fast => "KoukeiMotionDistanceSmall",
            MotionPreset.Standard => "KoukeiMotionDistancePlayer",
            MotionPreset.Panel => "KoukeiMotionDistancePanel",
            _ => "KoukeiMotionDistanceSmall"
        };

        try
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
                value is double distance)
            {
                return distance;
            }
        }
        catch
        {
        }

        return fallbackDistance;
    }

    public static async Task ShowAsync(
        UIElement element,
        MotionPreset preset = MotionPreset.Standard,
        MotionDirection direction = MotionDirection.Down,
        double? distance = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        var targetTranslation = Vector3.Zero;
        var state = BeginMotion(
            element,
            targetOpacity: 1,
            targetTranslation,
            targetVisibility: Visibility.Visible,
            targetHitTestVisible: true,
            pendingMutation: null,
            cancellationToken);

        element.Visibility = Visibility.Visible;
        element.IsHitTestVisible = true;

        if (!AnimationsEnabled)
        {
            CompleteMotionInstant(element, state);
            return;
        }

        var startTranslation = GetTranslation(direction, distance ?? GetDistance(preset));
        SetInstant(element, 0, startTranslation);
        await Task.Yield();

        if (!IsCurrent(element, state))
        {
            return;
        }

        var duration = GetDuration(preset);
        Animate(element, 1, duration, targetTranslation, duration);
        await CompleteAfterDelayAsync(element, state, duration);
    }

    public static async Task HideAsync(
        UIElement element,
        MotionPreset preset = MotionPreset.Standard,
        MotionDirection direction = MotionDirection.Up,
        double? distance = null,
        bool collapse = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);

        var targetTranslation = GetTranslation(direction, distance ?? GetDistance(preset));
        var state = BeginMotion(
            element,
            targetOpacity: 0,
            targetTranslation,
            targetVisibility: collapse ? Visibility.Collapsed : null,
            targetHitTestVisible: false,
            pendingMutation: null,
            cancellationToken);

        element.IsHitTestVisible = false;
        if (element.Visibility == Visibility.Collapsed || !AnimationsEnabled)
        {
            CompleteMotionInstant(element, state);
            return;
        }

        var duration = GetDuration(preset, isExit: true);
        Animate(element, 0, duration, targetTranslation, duration);
        await CompleteAfterDelayAsync(element, state, duration);
    }

    public static async Task CrossFadeAsync(
        UIElement? outgoing,
        UIElement? incoming,
        MotionPreset preset = MotionPreset.Standard,
        MotionDirection incomingDirection = MotionDirection.Down,
        CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(outgoing, incoming))
        {
            if (incoming is not null)
            {
                await ShowAsync(
                    incoming,
                    preset,
                    MotionDirection.None,
                    distance: 0,
                    cancellationToken: cancellationToken);
            }

            return;
        }

        var tasks = new List<Task>(2);
        if (outgoing is not null)
        {
            tasks.Add(HideAsync(
                outgoing,
                preset,
                MotionDirection.None,
                distance: 0,
                cancellationToken: cancellationToken));
        }

        if (incoming is not null)
        {
            tasks.Add(ShowAsync(
                incoming,
                preset,
                incomingDirection,
                cancellationToken: cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    public static async Task SwapContentAsync(
        UIElement element,
        Action updateContent,
        MotionPreset preset = MotionPreset.Fast,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(updateContent);

        var state = BeginMotion(
            element,
            targetOpacity: 1,
            targetTranslation: Vector3.Zero,
            targetVisibility: element.Visibility,
            targetHitTestVisible: element.IsHitTestVisible,
            pendingMutation: updateContent,
            cancellationToken);

        if (!AnimationsEnabled || element.Visibility == Visibility.Collapsed)
        {
            CompleteMotionInstant(element, state);
            return;
        }

        var totalDuration = GetDuration(preset);
        var halfDuration = TimeSpan.FromTicks(Math.Max(1, totalDuration.Ticks / 2));
        Animate(element, 0, halfDuration);

        if (!await DelayWhileCurrentAsync(element, state, halfDuration))
        {
            return;
        }

        ApplyPendingMutation(state);
        Animate(element, 1, halfDuration);
        await CompleteAfterDelayAsync(element, state, halfDuration);
    }

    public static void BringIntoView(
        ListViewBase list,
        object item,
        MotionIntent intent,
        ScrollIntoViewAlignment fallbackAlignment = ScrollIntoViewAlignment.Leading,
        double verticalAlignmentRatio = 0)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(item);

        int requestVersion;
        lock (MotionGate)
        {
            requestVersion = unchecked(++_nextListPositionVersion);
            ActiveListPositionRequests[list] = requestVersion;
        }

        var container = list.ContainerFromItem(item) as FrameworkElement;
        var scrollViewer = FindDescendantScrollViewer(list);
        var allowAnimatedBringIntoView =
            AnimationsEnabled &&
            intent is MotionIntent.Interactive or MotionIntent.PlaybackFollow &&
            list.ActualHeight > 0 &&
            container is not null &&
            scrollViewer is not null;

        if (allowAnimatedBringIntoView &&
            container is not null &&
            scrollViewer is not null)
        {
            try
            {
                var position = container
                    .TransformToVisual(scrollViewer)
                    .TransformPoint(new Windows.Foundation.Point());
                var viewportHeight = Math.Max(1, scrollViewer.ViewportHeight);
                var twoViewports = viewportHeight * 2;
                var isNearViewport =
                    position.Y >= -twoViewports &&
                    position.Y <= viewportHeight + twoViewports;

                if (isNearViewport)
                {
                    var targetOffset = CalculateListVerticalOffset(
                        scrollViewer,
                        container,
                        verticalAlignmentRatio);
                    if (scrollViewer.ChangeView(
                            horizontalOffset: null,
                            verticalOffset: targetOffset,
                            zoomFactor: null,
                            disableAnimation: false))
                    {
                        // ChangeView targets the internal ListView viewport directly.
                        // A second instant correction would introduce a visible
                        // one-frame jump at the end of the smooth scroll.
                        CompleteListPositionRequest(list, requestVersion);
                        return;
                    }
                }
            }
            catch
            {
                // Fall back to ListViewBase positioning if the container or its
                // internal ScrollViewer changed during virtualization.
            }
        }

        list.ScrollIntoView(item, fallbackAlignment);
        if (verticalAlignmentRatio > 0 &&
            fallbackAlignment == ScrollIntoViewAlignment.Default)
        {
            if (!list.DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsCurrentListPositionRequest(list, requestVersion))
                {
                    return;
                }

                SetInternalListPositionInstant(list, item, verticalAlignmentRatio);
                CompleteListPositionRequest(list, requestVersion);
            }))
            {
                CompleteListPositionRequest(list, requestVersion);
            }
            return;
        }

        CompleteListPositionRequest(list, requestVersion);
    }

    private static void SetInternalListPositionInstant(
        ListViewBase list,
        object item,
        double verticalAlignmentRatio)
    {
        if (!list.Items.Contains(item))
        {
            return;
        }

        if (list.ContainerFromItem(item) is not FrameworkElement container)
        {
            list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
            return;
        }

        var scrollViewer = FindDescendantScrollViewer(list);
        if (scrollViewer is null)
        {
            list.ScrollIntoView(item, ScrollIntoViewAlignment.Leading);
            return;
        }

        var targetOffset = CalculateListVerticalOffset(
            scrollViewer,
            container,
            verticalAlignmentRatio);
        scrollViewer.ChangeView(
            horizontalOffset: null,
            verticalOffset: targetOffset,
            zoomFactor: null,
            disableAnimation: true);
    }

    private static double CalculateListVerticalOffset(
        ScrollViewer scrollViewer,
        FrameworkElement container,
        double verticalAlignmentRatio)
    {
        var position = container
            .TransformToVisual(scrollViewer)
            .TransformPoint(new Windows.Foundation.Point());
        var ratio = Math.Clamp(verticalAlignmentRatio, 0, 1);
        var alignmentSpace = Math.Max(
            0,
            scrollViewer.ViewportHeight - container.ActualHeight);
        return Math.Max(
            0,
            scrollViewer.VerticalOffset + position.Y - (alignmentSpace * ratio));
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            if (FindDescendantScrollViewer(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static bool IsCurrentListPositionRequest(
        ListViewBase list,
        int requestVersion)
    {
        lock (MotionGate)
        {
            return ActiveListPositionRequests.TryGetValue(list, out var currentVersion) &&
                currentVersion == requestVersion;
        }
    }

    private static void CompleteListPositionRequest(
        ListViewBase list,
        int requestVersion)
    {
        lock (MotionGate)
        {
            if (ActiveListPositionRequests.TryGetValue(list, out var currentVersion) &&
                currentVersion == requestVersion)
            {
                ActiveListPositionRequests.Remove(list);
            }
        }
    }

    public static async Task AnimateEntranceAsync(
        IReadOnlyList<UIElement> elements,
        int maximumCount = 8,
        TimeSpan? staggerDelay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var count = Math.Min(Math.Max(0, maximumCount), elements.Count);
        if (count == 0)
        {
            return;
        }

        if (!AnimationsEnabled)
        {
            for (var index = 0; index < count; index++)
            {
                SetInstant(elements[index], 1, Vector3.Zero);
            }

            return;
        }

        try
        {
            var delay = staggerDelay ?? TimeSpan.FromMilliseconds(20);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = ShowAsync(
                    elements[index],
                    MotionPreset.Standard,
                    MotionDirection.Down,
                    distance: 8,
                    cancellationToken: cancellationToken);

                if (index + 1 < count)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Entrance animations are visual-only and are commonly started without
            // awaiting them. Cancellation must not surface as an unobserved task.
        }
    }

    public static async Task AnimateVisibleItemsEntranceAsync(
        ListViewBase list,
        int maximumCount = 8,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (!AnimationsEnabled || list.Items.Count == 0)
        {
            return;
        }

        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var firstVisibleIndex = list.ItemsPanelRoot switch
            {
                ItemsStackPanel itemsStackPanel => itemsStackPanel.FirstVisibleIndex,
                ItemsWrapGrid itemsWrapGrid => itemsWrapGrid.FirstVisibleIndex,
                _ => 0
            };
            firstVisibleIndex = Math.Max(0, firstVisibleIndex);

            var containers = new List<UIElement>(Math.Max(0, maximumCount));
            for (var index = firstVisibleIndex;
                 index < list.Items.Count && containers.Count < maximumCount;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (list.ContainerFromIndex(index) is UIElement container)
                {
                    containers.Add(container);
                }
                else if (containers.Count > 0)
                {
                    break;
                }
            }

            await AnimateEntranceAsync(
                containers,
                maximumCount,
                staggerDelay: TimeSpan.FromMilliseconds(20),
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // See AnimateEntranceAsync: cancellation is an expected latest-wins path.
        }
    }

    public static void Animate(
        UIElement element,
        double opacity,
        TimeSpan opacityDuration,
        Vector3? translation = null,
        TimeSpan? translationDuration = null)
    {
        if (!AnimationsEnabled)
        {
            SetInstant(element, opacity, translation);
            return;
        }

        element.OpacityTransition ??= new ScalarTransition();
        element.OpacityTransition.Duration = opacityDuration;

        if (translation is not null)
        {
            element.TranslationTransition ??= new Vector3Transition();
            element.TranslationTransition.Duration = translationDuration ?? opacityDuration;
        }

        element.Opacity = opacity;
        if (translation is { } targetTranslation)
        {
            element.Translation = targetTranslation;
        }
    }

    public static void SetInstant(
        UIElement element,
        double opacity,
        Vector3? translation = null)
    {
        var opacityTransition = element.OpacityTransition;
        var translationTransition = element.TranslationTransition;

        element.OpacityTransition = null;
        element.TranslationTransition = null;
        element.Opacity = opacity;
        if (translation is { } targetTranslation)
        {
            element.Translation = targetTranslation;
        }

        element.OpacityTransition = opacityTransition;
        element.TranslationTransition = translationTransition;
    }

    public static void SetVisibleInstant(UIElement element, bool isVisible, bool isHitTestVisible = true)
    {
        CancelMotion(element);
        element.IsHitTestVisible = isVisible && isHitTestVisible;
        element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        SetInstant(element, isVisible ? 1 : 0, Vector3.Zero);
    }

    public static void CancelMotion(UIElement element)
    {
        lock (MotionGate)
        {
            if (!ActiveMotions.Remove(element, out var state))
            {
                return;
            }

            state.Cancellation.Cancel();
            state.Cancellation.Dispose();
        }
    }

    private static ElementMotionState BeginMotion(
        UIElement element,
        double targetOpacity,
        Vector3? targetTranslation,
        Visibility? targetVisibility,
        bool? targetHitTestVisible,
        Action? pendingMutation,
        CancellationToken cancellationToken)
    {
        lock (MotionGate)
        {
            if (ActiveMotions.Remove(element, out var previousState))
            {
                previousState.Cancellation.Cancel();
                previousState.Cancellation.Dispose();
            }

            var linkedCancellation = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : new CancellationTokenSource();
            var state = new ElementMotionState
            {
                Version = unchecked(++_nextMotionVersion),
                Cancellation = linkedCancellation,
                TargetOpacity = targetOpacity,
                TargetTranslation = targetTranslation,
                TargetVisibility = targetVisibility,
                TargetHitTestVisible = targetHitTestVisible,
                PendingMutation = pendingMutation
            };
            ActiveMotions[element] = state;
            return state;
        }
    }

    private static async Task CompleteAfterDelayAsync(
        UIElement element,
        ElementMotionState state,
        TimeSpan duration)
    {
        if (await DelayWhileCurrentAsync(element, state, duration))
        {
            CompleteMotionInstant(element, state);
        }
    }

    private static async Task<bool> DelayWhileCurrentAsync(
        UIElement element,
        ElementMotionState state,
        TimeSpan duration)
    {
        try
        {
            await Task.Delay(duration, state.Cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AbandonMotion(element, state);
            return false;
        }

        return IsCurrent(element, state);
    }

    private static bool IsCurrent(UIElement element, ElementMotionState state)
    {
        lock (MotionGate)
        {
            return ActiveMotions.TryGetValue(element, out var currentState) &&
                ReferenceEquals(currentState, state) &&
                currentState.Version == state.Version;
        }
    }

    private static void CompleteMotionInstant(UIElement element, ElementMotionState state)
    {
        lock (MotionGate)
        {
            if (!ActiveMotions.TryGetValue(element, out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            ActiveMotions.Remove(element);
        }

        ApplyPendingMutation(state);
        SetInstant(element, state.TargetOpacity, state.TargetTranslation);
        if (state.TargetVisibility is { } visibility)
        {
            element.Visibility = visibility;
        }

        if (state.TargetHitTestVisible is { } isHitTestVisible)
        {
            element.IsHitTestVisible = isHitTestVisible;
        }

        state.Cancellation.Dispose();
    }

    private static void AbandonMotion(UIElement element, ElementMotionState state)
    {
        lock (MotionGate)
        {
            if (!ActiveMotions.TryGetValue(element, out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return;
            }

            ActiveMotions.Remove(element);
        }

        state.Cancellation.Dispose();
    }

    private static void ApplyPendingMutation(ElementMotionState state)
    {
        var mutation = Interlocked.Exchange(ref state.PendingMutation, null);
        mutation?.Invoke();
    }

    private static Vector3 GetTranslation(MotionDirection direction, double distance) =>
        direction switch
        {
            MotionDirection.Up => new Vector3(0, (float)-distance, 0),
            MotionDirection.Down => new Vector3(0, (float)distance, 0),
            MotionDirection.Left => new Vector3((float)-distance, 0, 0),
            MotionDirection.Right => new Vector3((float)distance, 0, 0),
            _ => Vector3.Zero
        };

    private static void UiSettings_AnimationsEnabledChanged(UISettings sender, object args)
    {
        bool animationsEnabled;
        try
        {
            animationsEnabled = sender.AnimationsEnabled;
        }
        catch
        {
            return;
        }

        if (animationsEnabled)
        {
            return;
        }

        KeyValuePair<UIElement, ElementMotionState>[] activeMotions;
        lock (MotionGate)
        {
            activeMotions = [.. ActiveMotions];
        }

        foreach (var (element, state) in activeMotions)
        {
            _ = element.DispatcherQueue.TryEnqueue(() =>
            {
                state.Cancellation.Cancel();
                CompleteMotionInstant(element, state);
            });
        }
    }
}
