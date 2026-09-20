using CsmForge.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;

namespace CsmForge.Runtime.Cities1
{
    internal sealed class ForgePlayerPresenceUi : MonoBehaviour
    {
        private sealed class CursorView
        {
            public UILabel Label;
            public UITextureSprite Cursor;
        }

        private readonly Dictionary<Guid, CursorView> views = new Dictionary<Guid, CursorView>();
        private MethodInfo rayCast;
        private float nextPublish;

        private void Update()
        {
            MultiplayerStatusSnapshot status = RuntimeServices.Multiplayer.Status;
            bool active = status.Mode == MultiplayerSessionMode.Hosting || status.Mode == MultiplayerSessionMode.ClientLive;
            if (!active) { SetAllVisible(false); return; }
            if (Time.realtimeSinceStartup >= nextPublish)
            {
                nextPublish = Time.realtimeSinceStartup + 0.10f;
                PublishLocal();
            }
            Render(status.Presentations);
        }

        private void PublishLocal()
        {
            ToolController controller = ToolsModifierControl.toolController;
            ToolBase tool = controller == null ? null : controller.CurrentTool;
            Vector3 world = Vector3.zero;
            bool visible = tool != null && TryWorldPosition(out world);
            if (!visible) world = Vector3.zero;
            RuntimeServices.Multiplayer.TryPublishPresentation(tool == null ? string.Empty : tool.GetType().FullName,
                world.x, world.y, world.z, visible);
        }

        private bool TryWorldPosition(out Vector3 world)
        {
            world = Vector3.zero;
            Camera camera = Camera.main;
            if (camera == null) return false;
            if (rayCast == null) rayCast = ResolveRayCast();
            if (rayCast == null) return false;
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            object[] parameters = { new ToolBase.RaycastInput(ray, camera.farClipPlane), null };
            try
            {
                if (!(bool)rayCast.Invoke(null, parameters)) return false;
                world = ((ToolBase.RaycastOutput)parameters[1]).m_hitPos;
                return true;
            }
            catch { return false; }
        }

        private static MethodInfo ResolveRayCast()
        {
            MethodInfo[] methods = typeof(ToolBase).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "RayCast" || methods[i].ReturnType != typeof(bool)) continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(ToolBase.RaycastInput) &&
                    parameters[1].ParameterType == typeof(ToolBase.RaycastOutput).MakeByRefType()) return methods[i];
            }
            return null;
        }

        private void Render(MultiplayerPresentationSnapshot[] values)
        {
            HashSet<Guid> present = new HashSet<Guid>();
            for (int i = 0; i < values.Length; i++)
            {
                MultiplayerPresentationSnapshot value = values[i];
                if (value.IsLocal) continue;
                present.Add(value.Member.MemberId);
                CursorView view;
                if (!views.TryGetValue(value.Member.MemberId, out view))
                { view = CreateView(); views.Add(value.Member.MemberId, view); }
                view.Label.text = value.DisplayName + ToolCaption(value.ToolName);
                view.Label.isVisible = value.Visible; view.Cursor.isVisible = value.Visible;
                SetCursorTexture(view.Cursor, value.ToolName);
                if (value.Visible) Position(view, new Vector3(value.WorldX, value.WorldY, value.WorldZ));
            }
            foreach (KeyValuePair<Guid, CursorView> pair in views)
                if (!present.Contains(pair.Key)) { pair.Value.Label.isVisible = false; pair.Value.Cursor.isVisible = false; }
        }

        private static string ToolCaption(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return string.Empty;
            int dot = toolName.LastIndexOf('.');
            return "\n" + (dot >= 0 ? toolName.Substring(dot + 1) : toolName);
        }

        private static CursorView CreateView()
        {
            UIView ui = UIView.GetAView();
            UILabel label = (UILabel)ui.AddUIComponent(typeof(UILabel));
            label.textAlignment = UIHorizontalAlignment.Center; label.verticalAlignment = UIVerticalAlignment.Middle;
            label.backgroundSprite = "CursorInfoBack"; label.padding = new RectOffset(38, 18, 5, 5);
            label.textScale = 0.75f; label.autoSize = true; label.isInteractive = false; label.isVisible = false;
            UITextureSprite cursor = (UITextureSprite)ui.AddUIComponent(typeof(UITextureSprite));
            cursor.size = new Vector2(24, 24); cursor.isInteractive = false; cursor.isVisible = false;
            return new CursorView { Label = label, Cursor = cursor };
        }

        private static void Position(CursorView view, Vector3 world)
        {
            UIView ui = view.Label.GetUIView(); Camera camera = Camera.main; if (camera == null) return;
            Vector3 screen = camera.WorldToScreenPoint(world); screen /= ui.inputScale;
            if (screen.z < 0) screen.Scale(-Vector3.one);
            Vector3 relative = ui.ScreenPointToGUI(screen);
            Vector2 size = ToolBase.fullscreenContainer == null ? ui.GetScreenResolution() : ToolBase.fullscreenContainer.size;
            relative.x = Mathf.Clamp(relative.x, 12, Math.Max(12, size.x - view.Label.width - 12));
            relative.y = Mathf.Clamp(relative.y, 12, Math.Max(12, size.y - view.Label.height - 12));
            view.Label.relativePosition = relative; view.Cursor.relativePosition = relative + new Vector3(8, 8);
        }

        private static void SetCursorTexture(UITextureSprite sprite, string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return;
            ToolController controller = ToolsModifierControl.toolController; if (controller == null) return;
            ToolBase[] tools = controller.GetComponents<ToolBase>();
            for (int i = 0; i < tools.Length; i++)
            {
                if (tools[i] == null || tools[i].GetType().FullName != toolName) continue;
                FieldInfo cursorField = FindField(tools[i].GetType(), "m_cursor");
                CursorInfo cursor = cursorField == null ? null : cursorField.GetValue(tools[i]) as CursorInfo;
                if (cursor != null && cursor.m_texture != null) sprite.texture = cursor.m_texture;
                return;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field;
                type = type.BaseType;
            }
            return null;
        }

        private void SetAllVisible(bool visible)
        {
            foreach (CursorView view in views.Values)
            { view.Label.isVisible = visible; view.Cursor.isVisible = visible; }
        }

        private void OnDestroy()
        {
            foreach (CursorView view in views.Values)
            {
                if (view.Label != null) Destroy(view.Label.gameObject);
                if (view.Cursor != null) Destroy(view.Cursor.gameObject);
            }
            views.Clear();
        }
    }
}
