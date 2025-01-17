using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Timeline;
using MenuEntryPair = System.Collections.Generic.KeyValuePair<UnityEngine.GUIContent, UnityEditor.Timeline.TimelineAction>;

namespace UnityEditor.Timeline
{
    abstract class TimelineAction : MenuItemActionBase
    {
        public abstract bool Execute(WindowState state);

        public virtual MenuActionDisplayState GetDisplayState(WindowState state)
        {
            return MenuActionDisplayState.Visible;
        }

        public virtual bool IsChecked(WindowState state)
        {
            return false;
        }

        bool CanExecute(WindowState state)
        {
            return GetDisplayState(state) == MenuActionDisplayState.Visible;
        }

        public static void Invoke<T>(WindowState state) where T : TimelineAction
        {
            var action = actions.FirstOrDefault(x => x.GetType() == typeof(T));
            if (action != null && action.CanExecute(state))
                action.Execute(state);
        }

        protected Vector2? m_MousePosition;

        internal void SetMousePosition(Vector2 pos)
        {
            m_MousePosition = pos;
        }

        internal void ClearMousePosition()
        {
            m_MousePosition = null;
        }

        static List<TimelineAction> s_ActionClasses;

        static List<TimelineAction> actions
        {
            get
            {
                return s_ActionClasses ?? (s_ActionClasses = GetActionsOfType(typeof(TimelineAction))
                        .Select(x => (TimelineAction)x.GetConstructors()[0].Invoke(null))
                        .ToList());
            }
        }

        public static void AddToMenu(GenericMenu menu, WindowState state, Vector2 mousePos)
        {
            foreach (var entry in GetMenuEntries(state, mousePos))
            {
                if (entry is MenuEntryPair)
                {
                    var menuEntry = (MenuEntryPair)entry;
                    var gui = menuEntry.Key;
                    var action = menuEntry.Value;
                    menu.AddItem(gui, action.IsChecked(state), f =>
                    {
                        action.SetMousePosition(mousePos);
                        action.Execute(state);
                        action.ClearMousePosition();
                    }, action);
                }
                else if (entry is GUIContent)
                {
                    menu.AddDisabledItem((GUIContent)entry);
                }
                else if (entry is string)
                {
                    menu.AddSeparator((string)entry);
                }
            }

            GetMenuEntries(state, mousePos);
        }

        public static List<object> GetMenuEntries(WindowState state, Vector2 mousePos)
        {
            var ret = new List<object>();
            actions.ForEach(action =>
            {
                string subMenuPath;
                var categoryAttr = GetCategoryAttribute(action);

                if (categoryAttr == null)
                    subMenuPath = string.Empty;
                else
                {
                    subMenuPath = categoryAttr.Category;
                    if (!subMenuPath.EndsWith("/"))
                        subMenuPath += "/";
                }

                var displayName = GetDisplayName(action);
                action.SetMousePosition(mousePos);
                // The display state could be dependent on mouse position
                var displayState = action.GetDisplayState(state);
                action.ClearMousePosition();
                var menuItemName = subMenuPath + displayName;
                var separator = GetSeparator(action);
                var canBeAddedToMenu = !TypeUtility.IsHiddenInMenu(action.GetType()) && displayState != MenuActionDisplayState.Hidden;

                if (canBeAddedToMenu)
                {
                    if (separator != null && separator.before)
                    {
                        ret.Add(subMenuPath);
                    }

                    if (displayState == MenuActionDisplayState.Visible)
                    {
                        var entry = new MenuEntryPair(new GUIContent(menuItemName), action);
                        ret.Add(entry);
                    }

                    if (displayState == MenuActionDisplayState.Disabled)
                    {
                        ret.Add(new GUIContent(menuItemName));
                    }

                    if (separator != null && separator.after)
                    {
                        ret.Add(subMenuPath);
                    }
                }
            });

            return ret;
        }

        public static bool HandleShortcut(WindowState state, Event evt)
        {
            if (EditorGUI.IsEditingTextField())
                return false;

            foreach (var action in actions)
            {
                var attr = action.GetType().GetCustomAttributes(typeof(ShortcutAttribute), true);

                foreach (ShortcutAttribute shortcut in attr)
                {
                    if (shortcut.MatchesEvent(evt))
                    {
                        if (s_ShowActionTriggeredByShortcut)
                            Debug.Log(action.GetType().Name);

                        var handled = action.Execute(state);
                        if (handled)
                            return true;
                    }
                }
            }

            return false;
        }

        protected static bool DoInternal(Type t, WindowState state)
        {
            var action = (TimelineAction)t.GetConstructors()[0].Invoke(null);

            if (action.CanExecute(state))
                return action.Execute(state);

            return false;
        }
    }

    [DisplayName("Copy")]
    [Shortcut("Main Menu/Edit/Copy", EventCommandNames.Copy)]
    class CopyAction : TimelineAction
    {
        public override MenuActionDisplayState GetDisplayState(WindowState state)
        {
            return SelectionManager.Count() > 0 ? MenuActionDisplayState.Visible : MenuActionDisplayState.Disabled;
        }

        public override bool Execute(WindowState state)
        {
            TimelineEditor.clipboard.Clear();

            var clips = SelectionManager.SelectedClips().ToArray();
            if (clips.Length > 0)
            {
                ItemAction<TimelineClip>.Invoke<CopyClipsToClipboard>(state, clips);
            }
            var markers = SelectionManager.SelectedMarkers().ToArray();
            if (markers.Length > 0)
            {
                ItemAction<IMarker>.Invoke<CopyMarkersToClipboard>(state, markers);
            }
            var tracks = SelectionManager.SelectedTracks().ToArray();
            if (tracks.Length > 0)
            {
                CopyTracksToClipboard.Do(state, tracks);
            }

            return true;
        }
    }

    [DisplayName("Paste")]
    [Shortcut("Main Menu/Edit/Paste", EventCommandNames.Paste)]
    class PasteAction : TimelineAction
    {
        public static bool Do(WindowState state)
        {
            return DoInternal(typeof(PasteAction), state);
        }

        public override MenuActionDisplayState GetDisplayState(WindowState state)
        {
            return CanPaste(state) ? MenuActionDisplayState.Visible : MenuActionDisplayState.Disabled;
        }

        public override bool Execute(WindowState state)
        {
            if (!CanPaste(state))
                return false;

            PasteItems(state, m_MousePosition);
            PasteTracks(state);

            state.Refresh();

            ClearMousePosition();
            return true;
        }

        bool CanPaste(WindowState state)
        {
            var copiedItems = TimelineEditor.clipboard.GetCopiedItems().ToList();

            if (!copiedItems.Any())
                return TimelineEditor.clipboard.GetTracks().Any();

            return CanPasteItems(copiedItems, state, m_MousePosition);
        }

        static bool CanPasteItems(ICollection<ItemsPerTrack> itemsGroups, WindowState state, Vector2? mousePosition)
        {
            var hasItemsCopiedFromMultipleTracks = itemsGroups.Count > 1;
            var allItemsCopiedFromCurrentAsset = itemsGroups.All(x => x.targetTrack.timelineAsset == state.editSequence.asset);
            var hasUsedShortcut = mousePosition == null;
            var anySourceLocked = itemsGroups.Any(x => x.targetTrack != null && x.targetTrack.lockedInHierarchy);

            //do not paste if the user copied clips from another timeline
            //if the copied clips comes from > 1 track (since we do not know where to paste the copied clips)
            //or if a keyboard shortcut was used (since the user will not see the paste result)
            if (!allItemsCopiedFromCurrentAsset)
            {
                if (hasItemsCopiedFromMultipleTracks || hasUsedShortcut)
                    return false;
            }

            if (hasUsedShortcut)
            {
                return !anySourceLocked; // copy/paste to same track
            }

            var targetTrack = state.spacePartitioner.GetItemsAtPosition<IRowGUI>(mousePosition.Value).ToList();

            if (hasItemsCopiedFromMultipleTracks)
            {
                //do not paste if the track which received the paste action does not contain a copied clip
                return !anySourceLocked && itemsGroups.Select(x => x.targetTrack).Contains(targetTrack.First().asset);
            }

            //do not paste if the track which received the paste action is not of the right type
            var unlockedTargetTracks = targetTrack.Where(t => !t.asset.lockedInHierarchy).Select(track => track.asset);
            var copiedItems = itemsGroups.SelectMany(i => i.items);
            return unlockedTargetTracks.Any(t => copiedItems.All(i => i.IsCompatibleWithTrack(t)));
        }

        static void PasteItems(WindowState state, Vector2? mousePosition)
        {
            var copiedItems = TimelineEditor.clipboard.GetCopiedItems().ToList();
            var numberOfUniqueParentsInClipboard = copiedItems.Count();

            if (numberOfUniqueParentsInClipboard == 0)
                return;
            List<ITimelineItem> newItems;
            //if the copied items were on a single parent, then use the mouse position to get the parent OR the original parent
            if (numberOfUniqueParentsInClipboard == 1)
            {
                var itemsGroup = copiedItems.First();
                TrackAsset target;
                if (mousePosition.HasValue)
                    target = state.spacePartitioner.GetItemsAtPosition<IRowGUI>(mousePosition.Value).First().asset;
                else
                    target = FindSuitableParentForSingleTrackPasteWithoutMouse(itemsGroup);

                var candidateTime = TimelineHelpers.GetCandidateTime(state, mousePosition, target);
                newItems = TimelineHelpers.DuplicateItemsUsingCurrentEditMode(state, itemsGroup, target, candidateTime, "Paste Items").ToList();
            }
            //if copied items were on multiple parents, then the destination parents are the same as the original parents
            else
            {
                var time = TimelineHelpers.GetCandidateTime(state, mousePosition, copiedItems.Select(c => c.targetTrack).ToArray());
                newItems = TimelineHelpers.DuplicateItemsUsingCurrentEditMode(state, copiedItems, time, "Paste Items").ToList();
            }


            TimelineHelpers.FrameItems(state, newItems);

            SelectionManager.RemoveTimelineSelection();
            foreach (var item in newItems)
            {
                SelectionManager.Add(item);
            }
        }

        static TrackAsset FindSuitableParentForSingleTrackPasteWithoutMouse(ItemsPerTrack itemsGroup)
        {
            var groupParent = itemsGroup.targetTrack; //set a main parent in the clipboard
            var selectedTracks = SelectionManager.SelectedTracks();

            if (selectedTracks.Contains(groupParent))
            {
                return groupParent;
            }

            //find a selected track suitable for all items
            var itemsToPaste = itemsGroup.items;
            var compatibleTrack = selectedTracks.Where(t => !t.lockedInHierarchy).FirstOrDefault(t => itemsToPaste.All(i => i.IsCompatibleWithTrack(t)));

            return compatibleTrack != null ? compatibleTrack : groupParent;
        }

        static void PasteTracks(WindowState state)
        {
            var trackData = TimelineEditor.clipboard.GetTracks().ToList();
            if (trackData.Any())
            {
                SelectionManager.RemoveTimelineSelection();
            }

            foreach (var track in trackData)
            {
                var newTrack = track.item.Duplicate(state.editSequence.director, state.editSequence.asset);
                SelectionManager.Add(newTrack);
                foreach (var childTrack in newTrack.GetFlattenedChildTracks())
                {
                    SelectionManager.Add(childTrack);
                }

                if (track.parent != null && track.parent.timelineAsset == state.editSequence.asset)
                {
                    track.parent.AddChild(newTrack);
                }
            }
        }
    }

    [DisplayName("Duplicate")]
    [Shortcut("Main Menu/Edit/Duplicate", EventCommandNames.Duplicate)]
    class DuplicateAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return Execute(state, (item1, item2) => ItemsUtils.TimeGapBetweenItems(item1, item2, state));
        }

        internal bool Execute(WindowState state, Func<ITimelineItem, ITimelineItem, double> gapBetweenItems)
        {
            var selectedItems = SelectionManager.SelectedItems().ToItemsPerTrack().ToList();
            if (selectedItems.Any())
            {
                var requestedTime = CalculateDuplicateTime(selectedItems, gapBetweenItems);
                var duplicatedItems = TimelineHelpers.DuplicateItemsUsingCurrentEditMode(state, selectedItems, requestedTime, "Duplicate Items");

                TimelineHelpers.FrameItems(state, duplicatedItems);
                SelectionManager.RemoveTimelineSelection();
                foreach (var item in duplicatedItems)
                    SelectionManager.Add(item);
            }

            var tracks = SelectionManager.SelectedTracks().ToArray();
            if (tracks.Length > 0)
                TrackAction.Invoke<DuplicateTracks>(state, tracks);

            state.Refresh();
            return true;
        }

        static double CalculateDuplicateTime(IEnumerable<ItemsPerTrack> duplicatedItems, Func<ITimelineItem, ITimelineItem, double> gapBetweenItems)
        {
            //Find the end time of the rightmost item
            var itemsOnTracks = duplicatedItems.SelectMany(i => i.targetTrack.GetItems()).ToList();
            var time = itemsOnTracks.Max(i => i.end);

            //From all the duplicated items, select the leftmost items
            var firstDuplicatedItems = duplicatedItems.Select(i => i.leftMostItem);
            var leftMostDuplicatedItems = firstDuplicatedItems.OrderBy(i => i.start).GroupBy(i => i.start).FirstOrDefault();
            if (leftMostDuplicatedItems == null) return 0.0;

            foreach (var leftMostItem in leftMostDuplicatedItems)
            {
                var siblings = leftMostItem.parentTrack.GetItems();
                var rightMostSiblings = siblings.OrderByDescending(i => i.end).GroupBy(i => i.end).FirstOrDefault();
                if (rightMostSiblings == null) continue;

                foreach (var sibling in rightMostSiblings)
                    time = Math.Max(time, sibling.end + gapBetweenItems(leftMostItem, sibling));
            }

            return time;
        }
    }

    [DisplayName("Delete")]
    [Shortcut("Main Menu/Edit/Delete", EventCommandNames.Delete)]
    [ShortcutPlatformOverride(RuntimePlatform.OSXEditor, KeyCode.Backspace, ShortcutModifiers.Action)]
    class DeleteAction : TimelineAction
    {
        public override MenuActionDisplayState GetDisplayState(WindowState state)
        {
            return CanDelete() ? MenuActionDisplayState.Visible : MenuActionDisplayState.Disabled;
        }

        static bool CanDelete()
        {
            // All() returns true when empty
            return SelectionManager.SelectedTracks().All(x => !x.lockedInHierarchy) &&
                SelectionManager.SelectedItems().All(x => x.parentTrack == null || !x.parentTrack.lockedInHierarchy);
        }

        public override bool Execute(WindowState state)
        {
            if (SelectionManager.GetCurrentInlineEditorCurve() != null)
                return false;

            if (!CanDelete())
                return false;

            var selectedItems = SelectionManager.SelectedItems();
            DeleteItems(selectedItems);

            var tracks = SelectionManager.SelectedTracks().ToArray();
            if (tracks.Any())
                TrackAction.Invoke<DeleteTracks>(state, tracks);

            state.Refresh();
            return selectedItems.Any() ||  tracks.Length > 0;
        }

        internal static void DeleteItems(IEnumerable<ITimelineItem> items)
        {
            var tracks = items.GroupBy(c => c.parentTrack);

            foreach (var track in tracks)
                TimelineUndo.PushUndo(track.Key, "Delete Items");

            TimelineAnimationUtilities.UnlinkAnimationWindowFromClips(items.OfType<ClipItem>().Select(i => i.clip));

            EditMode.PrepareItemsDelete(ItemsUtils.ToItemsPerTrack(items));
            EditModeUtils.Delete(items);

            SelectionManager.RemoveAllClips();
        }
    }

    [DisplayName("Match Content")]
    [Shortcut(Shortcuts.Timeline.matchContent)]
    class MatchContent : TimelineAction
    {
        public override MenuActionDisplayState GetDisplayState(WindowState state)
        {
            var clips = SelectionManager.SelectedClips().ToArray();

            if (!clips.Any() || SelectionManager.GetCurrentInlineEditorCurve() != null)
                return MenuActionDisplayState.Hidden;

            return clips.Any(TimelineHelpers.HasUsableAssetDuration)
                ? MenuActionDisplayState.Visible
                : MenuActionDisplayState.Disabled;
        }

        public override bool Execute(WindowState state)
        {
            if (SelectionManager.GetCurrentInlineEditorCurve() != null)
                return false;

            var clips = SelectionManager.SelectedClips().ToArray();
            return clips.Length > 0 && ClipModifier.MatchContent(clips);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.play)]
    class PlayTimelineAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            var currentState = state.playing;
            state.SetPlaying(!currentState);
            return true;
        }
    }

    [HideInMenu]
    class SelectAllAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            // otherwise select all tracks.
            SelectionManager.Clear();
            state.GetWindow().allTracks.ForEach(x => SelectionManager.Add(x.track));

            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.previousFrame)]
    class PreviousFrameAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            state.editSequence.frame--;
            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.nextFrame)]
    class NextFrameAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            state.editSequence.frame++;
            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.frameAll)]
    class FrameAllAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            if (state.IsEditingASubItem())
                return false;

            var w = state.GetWindow();
            if (w == null || w.treeView == null)
                return false;

            var visibleTracks = w.treeView.visibleTracks.ToList();
            if (state.editSequence.asset != null && state.editSequence.asset.markerTrack != null)
                visibleTracks.Add(state.editSequence.asset.markerTrack);

            if (visibleTracks.Count == 0)
                return false;

            var startTime = float.MaxValue;
            var endTime = float.MinValue;

            foreach (var t in visibleTracks)
            {
                if (t == null)
                    continue;

                double trackStart, trackEnd;
                t.GetItemRange(out trackStart, out trackEnd);
                startTime = Mathf.Min(startTime, (float)trackStart);
                endTime = Mathf.Max(endTime, (float)(trackEnd));
            }

            if (startTime != float.MinValue)
            {
                FrameSelectedAction.FrameRange(startTime, endTime, state);
                return true;
            }

            return false;
        }
    }

    [HideInMenu]
    class FrameSelectedAction : TimelineAction
    {
        public static void FrameRange(float startTime, float endTime, WindowState state)
        {
            if (startTime > endTime)
            {
                return;
            }

            var halfDuration = endTime - Math.Max(0.0f, startTime);

            if (halfDuration > 0.0f)
            {
                state.SetTimeAreaShownRange(Mathf.Max(-10.0f, startTime - (halfDuration * 0.1f)),
                    endTime + (halfDuration * 0.1f));
            }
            else
            {
                // start == end
                // keep the zoom level constant, only pan the time area to center the item
                var currentRange = state.timeAreaShownRange.y - state.timeAreaShownRange.x;
                state.SetTimeAreaShownRange(startTime - currentRange / 2, startTime + currentRange / 2);
            }

            TimelineZoomManipulator.InvalidateWheelZoom();
            state.Evaluate();
        }

        public override bool Execute(WindowState state)
        {
            if (state.IsEditingASubItem())
                return false;

            if (SelectionManager.Count() == 0)
                return false;

            var startTime = float.MaxValue;
            var endTime = float.MinValue;

            var clips = SelectionManager.SelectedClipGUI();
            var markers = SelectionManager.SelectedMarkers();
            if (!clips.Any() && !markers.Any())
                return false;

            foreach (var c in clips)
            {
                startTime = Mathf.Min(startTime, (float)c.clip.start);
                endTime = Mathf.Max(endTime, (float)c.clip.end);
                if (c.clipCurveEditor != null)
                {
                    c.clipCurveEditor.FrameClip();
                }
            }

            foreach (var marker in markers)
            {
                startTime = Mathf.Min(startTime, (float)marker.time);
                endTime = Mathf.Max(endTime, (float)marker.time);
            }

            FrameRange(startTime, endTime, state);

            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.previousKey)]
    class PrevKeyAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            var keyTraverser = new Utilities.KeyTraverser(state.editSequence.asset, 0.01f / state.referenceSequence.frameRate);
            var time = keyTraverser.GetPrevKey((float)state.editSequence.time, state.dirtyStamp);
            if (time != state.editSequence.time)
            {
                state.editSequence.time = time;
            }

            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.nextKey)]
    class NextKeyAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            var keyTraverser = new Utilities.KeyTraverser(state.editSequence.asset, 0.01f / state.referenceSequence.frameRate);
            var time = keyTraverser.GetNextKey((float)state.editSequence.time, state.dirtyStamp);
            if (time != state.editSequence.time)
            {
                state.editSequence.time = time;
            }

            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.goToStart)]
    class GotoStartAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            state.editSequence.time = 0.0f;
            state.EnsurePlayHeadIsVisible();

            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.goToEnd)]
    class GotoEndAction : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            state.editSequence.time = state.editSequence.duration;
            state.EnsurePlayHeadIsVisible();

            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.zoomIn)]
    class ZoomIn : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            TimelineZoomManipulator.Instance.DoZoom(1.15f, state);
            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.zoomOut)]
    class ZoomOut : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            TimelineZoomManipulator.Instance.DoZoom(0.85f, state);
            return true;
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.collapseGroup)]
    class CollapseGroup : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.CollapseGroup(state);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.unCollapseGroup)]
    class UnCollapseGroup : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.UnCollapseGroup(state);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.selectLeftItem)]
    class SelectLeftClip : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            // Switches to track header if no left track exists
            return KeyboardNavigation.SelectLeftItem(state);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.selectRightItem)]
    class SelectRightClip : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectRightItem(state);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.selectUpItem)]
    class SelectUpClip : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectUpItem(state);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.selectUpTrack)]
    class SelectUpTrack : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectUpTrack();
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.selectDownItem)]
    class SelectDownClip : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectDownItem(state);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.selectDownTrack)]
    class SelectDownTrack : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            if (!KeyboardNavigation.ClipAreaActive() && !KeyboardNavigation.TrackHeadActive())
                return KeyboardNavigation.FocusFirstVisibleItem(state);
            else
                return KeyboardNavigation.SelectDownTrack();
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.multiSelectLeft)]
    class MultiselectLeftClip : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectLeftItem(state, true);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.multiSelectRight)]
    class MultiselectRightClip : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectRightItem(state, true);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.multiSelectUp)]
    class MultiselectUpTrack : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectUpTrack(true);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.multiSelectDown)]
    class MultiselectDownTrack : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            return KeyboardNavigation.SelectDownTrack(true);
        }
    }

    [HideInMenu]
    [Shortcut(Shortcuts.Timeline.toggleClipTrackArea)]
    class ToggleClipTrackArea : TimelineAction
    {
        public override bool Execute(WindowState state)
        {
            if (KeyboardNavigation.TrackHeadActive())
                return KeyboardNavigation.FocusFirstVisibleItem(state, SelectionManager.SelectedTracks());

            if (!KeyboardNavigation.ClipAreaActive())
                return KeyboardNavigation.FocusFirstVisibleItem(state);

            var item = KeyboardNavigation.GetVisibleSelectedItems().LastOrDefault();
            if (item != null)
                SelectionManager.SelectOnly(item.parentTrack);
            return true;
        }
    }

    [HideInMenu]
    [DisplayName("Mute")]
    class ToggleMuteMarkersOnTimeline : TimelineAction
    {
        public override bool IsChecked(WindowState state)
        {
            return IsMarkerTrackValid(state) && state.editSequence.asset.markerTrack.muted;
        }

        public override bool Execute(WindowState state)
        {
            if (state.showMarkerHeader)
                ToggleMute(state);
            return true;
        }

        static void ToggleMute(WindowState state)
        {
            var timeline = state.editSequence.asset;
            timeline.markerTrack.muted = !timeline.markerTrack.muted;
        }

        static bool IsMarkerTrackValid(WindowState state)
        {
            var timeline = state.editSequence.asset;
            return timeline != null && timeline.markerTrack != null;
        }
    }

    [HideInMenu]
    [DisplayName("Show Markers")]
    class ToggleShowMarkersOnTimeline : TimelineAction
    {
        public override bool IsChecked(WindowState state)
        {
            return state.showMarkerHeader;
        }

        public override bool Execute(WindowState state)
        {
            ToggleShow(state);
            return true;
        }

        static void ToggleShow(WindowState state)
        {
            state.showMarkerHeader = !state.showMarkerHeader;
        }
    }
}
