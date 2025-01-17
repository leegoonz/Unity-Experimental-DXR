using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace UnityEditor.Timeline
{
    class TimelinePanManipulator : Manipulator
    {
        bool m_Active;

        protected override bool MouseDown(Event evt, WindowState state)
        {
            if ((evt.button == 2 && evt.modifiers == EventModifiers.None) ||
                (evt.button == 0 && evt.modifiers == EventModifiers.Alt))
            {
                TimelineCursors.SetCursor(TimelineCursors.CursorType.Pan);

                m_Active = true;
                return true;
            }

            return false;
        }

        protected override bool MouseUp(Event evt, WindowState state)
        {
            if (m_Active)
            {
                TimelineCursors.ClearCursor();
                state.editorWindow.Repaint();
            }

            return false;
        }

        protected override bool MouseDrag(Event evt, WindowState state)
        {
            // Note: Do not rely on evt.button here as some 3rd party automation
            //       software does not properly set the button data during drag.

            if (!m_Active)
                return false;

            var cursorRect = TimelineWindow.instance.sequenceContentRect;
            cursorRect.xMax = TimelineWindow.instance.position.xMax;
            cursorRect.yMax = TimelineWindow.instance.position.yMax;

            if (state.GetWindow() != null && state.GetWindow().treeView != null)
            {
                var scroll = state.GetWindow().treeView.scrollPosition;
                scroll.y -= evt.delta.y;
                state.GetWindow().treeView.scrollPosition = scroll;
                state.OffsetTimeArea((int)evt.delta.x);
                return true;
            }

            return false;
        }
    }


    class TimelineZoomManipulator : Manipulator
    {
        Vector2 m_MouseDownPos = Vector2.zero;
        Vector2 m_InitialShownRange = Vector2.zero;
        float m_FocalTime;
        float m_LastMouseMoveX = -1;
        float m_ZoomFactor = 1;
        bool m_WheelUsedLast;

        TimelineZoomManipulator() {}

        public static readonly TimelineZoomManipulator Instance = new TimelineZoomManipulator();

        internal void DoZoom(float zoomFactor, WindowState state)
        {
            var refRange = state.timeAreaShownRange;
            DoZoom(zoomFactor, state, refRange, (refRange.x + refRange.y) / 2);
            // Force resetting the reference zoom after a Framing operation
            InvalidateWheelZoom();
        }

        static void DoZoom(float zoomFactor, WindowState state, Vector2 refRange, float focalTime)
        {
            const float kMinRange = 0.05f; // matches zoomable area.

            var s = zoomFactor;
            var t = focalTime;
            if (s <= 0) return;
            var x = (refRange.x + t * (s - 1)) / s;
            var y = (refRange.y + t * (s - 1)) / s;

            // don't set it if we reach the limit or panning happens
            if (Math.Abs(x - y) > kMinRange)
            {
                // Zoomable area does not protect 100% against crazy values
                state.SetTimeAreaShownRange(Math.Max(x, 0), Math.Min(y, WindowState.kMaxShownTime));
            }
        }

        internal static void InvalidateWheelZoom()
        {
            Instance.m_WheelUsedLast = false;
        }

        protected override bool MouseDown(Event evt, WindowState state)
        {
            m_MouseDownPos = evt.mousePosition;
            m_FocalTime = state.PixelToTime(m_MouseDownPos.x);
            m_InitialShownRange = state.timeAreaShownRange;
            return false;
        }

        protected override bool MouseWheel(Event evt, WindowState state)
        {
            if (Math.Abs(evt.delta.y) < 1e-5)
                return false;

            if (!m_WheelUsedLast || Mathf.Abs(m_LastMouseMoveX - evt.mousePosition.x) > 1.0f)
            {
                m_LastMouseMoveX = evt.mousePosition.x;
                m_FocalTime = state.PixelToTime(m_LastMouseMoveX);
                m_InitialShownRange = state.timeAreaShownRange;
                m_ZoomFactor = 1;
            }

            var newZoom = m_ZoomFactor * (-evt.delta.y * 0.02f + 1);
            newZoom = Mathf.Clamp(newZoom, 1e-7f, 1e7f);

            var lastRange = state.timeAreaShownRange;
            DoZoom(newZoom, state, m_InitialShownRange, m_FocalTime);

            // if we hit a limit, don't change the zoom
            //  this prevents accumulating when zoom doesn't change
            if (lastRange != state.timeAreaShownRange)
                m_ZoomFactor = newZoom;

            m_WheelUsedLast = true;
            return true;
        }

        protected override bool MouseDrag(Event evt, WindowState state)
        {
            // Fast zoom...
            if (evt.modifiers != EventModifiers.Alt || evt.button != 1) return false;

            var mouseMoveLength = Event.current.mousePosition - m_MouseDownPos;
            var delta = Math.Abs(mouseMoveLength.x) > Math.Abs(mouseMoveLength.y)
                ? mouseMoveLength.x
                : -mouseMoveLength.y;
            m_ZoomFactor = PixelToZoom(delta);
            DoZoom(m_ZoomFactor, state, m_InitialShownRange, m_FocalTime);

            m_WheelUsedLast = false;
            return true;
        }

        static float PixelToZoom(float x)
        {
            const float pixel2Zoom = 1 / 300.0f;
            x *=  pixel2Zoom;
            if (x < -0.75)
            {
                // Rational function that behaves like 1+x on [-0.75,inf) and assimptotically reaches zero on (-inf,-0.75]
                // The coefficients were obtained by the following constraints:
                //1) f(-0.75) = 0.25
                //2) f'(-0.75) = 1 C1 continuity
                //3) f(-3) = 0.001 (assimtotically zero)
                return 1 / (98.6667f + 268.444f * x + 189.63f * x * x);
            }
            return 1 + x;
        }
    }

    class TimelineShortcutManipulator : Manipulator
    {
        protected override bool ValidateCommand(Event evt, WindowState state)
        {
            return evt.commandName == EventCommandNames.Copy ||
                evt.commandName == EventCommandNames.Paste ||
                evt.commandName == EventCommandNames.Duplicate ||
                evt.commandName == EventCommandNames.SelectAll ||
                evt.commandName == EventCommandNames.Delete ||
                evt.commandName == EventCommandNames.SoftDelete ||
                evt.commandName == EventCommandNames.FrameSelected;
        }

        protected override bool ExecuteCommand(Event evt, WindowState state)
        {
            if (state.IsCurrentEditingASequencerTextField())
                return false;

            if (evt.commandName == EventCommandNames.SelectAll)
            {
                TimelineAction.Invoke<SelectAllAction>(state);
                return true;
            }

            if (evt.commandName == EventCommandNames.SoftDelete)
            {
                TimelineAction.Invoke<DeleteAction>(state);
                return true;
            }

            if (evt.commandName == EventCommandNames.FrameSelected)
            {
                TimelineAction.Invoke<FrameSelectedAction>(state);
                return true;
            }

            return TimelineAction.HandleShortcut(state, evt);
        }
    }
}
