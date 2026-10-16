using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PoEController.Menus;
using UnityEngine;

namespace PoEController.Context
{
    /// <summary>
    /// Release-log lines (<see cref="ModLog.Report"/>) for the party window in its "choose who comes" mode (leaving
    /// Caed Nua): when it opens, whether its Accept button can be reached by the controller (a d-pad stop, and a click
    /// on it lands on it), and how it closed. Players reported needing the free cursor there; their logs show why.
    /// At most a few lines per session.
    /// </summary>
    internal static class PartyWindowReport
    {
        private const int MaxReports = 10;

        private static readonly PropertyInfo s_Alternate = AccessTools.Property(typeof(UIHudWindow), "AlternateMode");
        private static readonly FieldInfo s_Accepted = AccessTools.Field(typeof(UIPartyManager), "m_Accept");

        private static UIHudWindow s_Top;
        private static int s_Reports;
        /// <summary>Opened, measured half a second later (its buttons are laid out by then).</summary>
        private static UIPartyManager s_Pending;
        private static float s_Wait;

        public static void Init()
        {
            ScreenTracker.TopChanged += OnTopChanged;
        }

        private static void OnTopChanged(UIHudWindow top)
        {
            UIHudWindow before = s_Top;
            s_Top = top;
            if (s_Reports >= MaxReports)
            {
                return;
            }
            try
            {
                if (top is UIPartyManager opened && Choosing(opened))
                {
                    s_Pending = opened;
                    s_Wait = 0.5f;
                }
                else if (before is UIPartyManager closed && top != before && !closed.IsVisible)
                {
                    s_Reports++;
                    bool accepted = s_Accepted != null && (bool)s_Accepted.GetValue(closed);
                    Plugin.Log.Report($"Party window closed ({(accepted ? "accepted" : "cancelled")})");
                }
            }
            catch (Exception e)
            {
                s_Reports = MaxReports;
                Plugin.Log.Report($"Party window report off: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Once per frame (router): the delayed "opened" line.</summary>
        public static void Update(float dt)
        {
            if (s_Pending == null)
            {
                return;
            }
            s_Wait -= dt;
            if (s_Wait > 0f)
            {
                return;
            }
            UIPartyManager window = s_Pending;
            s_Pending = null;
            if (s_Reports >= MaxReports || window == null || !window.IsVisible)
            {
                return;
            }
            try
            {
                s_Reports++;
                Plugin.Log.Report($"Party window (choose party) opened: {AcceptState(window)}");
            }
            catch (Exception e)
            {
                s_Reports = MaxReports;
                Plugin.Log.Report($"Party window report off: {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool Choosing(UIPartyManager window)
        {
            return s_Alternate != null && (bool)s_Alternate.GetValue(window, null);
        }

        /// <summary>Accept: shown?, a d-pad stop?, does a click on its centre land on it?</summary>
        private static string AcceptState(UIPartyManager window)
        {
            UIMultiSpriteImageButton accept = window.AcceptButton;
            if (accept == null || !accept.gameObject.activeInHierarchy)
            {
                return "Accept button not shown";
            }
            Collider c = accept.GetComponentInChildren<Collider>();
            Camera cam = c != null ? NGUITools.FindCameraForLayer(c.gameObject.layer) : null;
            if (c == null || !c.enabled || cam == null)
            {
                return "Accept button has no active collider";
            }
            Vector3 centre = cam.WorldToScreenPoint(c.bounds.center);
            Collider hit = FocusNavigator.ColliderAt(new Vector2(centre.x, centre.y));
            bool clickLands = hit != null && (hit == c || hit.transform.IsChildOf(accept.transform));
            Vector3 a = cam.WorldToScreenPoint(c.bounds.min);
            Vector3 b = cam.WorldToScreenPoint(c.bounds.max);
            Rect r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            bool stop = false;
            List<Vector2> points = FocusNavigator.Collect(window.transform, null);
            foreach (Vector2 p in points)
            {
                if (r.Contains(p))
                {
                    stop = true;
                    break;
                }
            }
            return $"Accept d-pad stop: {stop}, click lands on it: {clickLands}"
                + (clickLands ? "" : $" (covered by '{(hit != null ? hit.name : "nothing")}')") + $", {points.Count} stops";
        }
    }
}
