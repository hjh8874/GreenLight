using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.UI
{
    /// <summary>
    /// 여러 배치 컨트롤러의 건설 상태를 하나의 망치 커서로 통합합니다.
    /// 프로젝트 커서 에셋을 사용하고, 에셋 로딩 실패 시 런타임 커서를 생성합니다.
    /// </summary>
    internal static class BuildModeCursorFeedback
    {
        private const string HammerCursorResourcePath =
            "CityFlow/UI/Cursors/build_hammer_cursor";
        private const int CursorSize = 32;
        private static readonly Vector2 HammerHotspot = new Vector2(10f, 10f);
        private static readonly HashSet<EntityId> ActiveSources = new HashSet<EntityId>();

        private static Texture2D hammerCursor;
        private static bool isHammerApplied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            ActiveSources.Clear();
            hammerCursor = null;
            isHammerApplied = false;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        public static void SetBuilding(Object source, bool isBuilding)
        {
            if (source == null)
            {
                return;
            }

            EntityId sourceId = source.GetEntityId();
            if (isBuilding)
            {
                ActiveSources.Add(sourceId);
            }
            else
            {
                ActiveSources.Remove(sourceId);
            }

            ApplyCursor();
        }

        private static void ApplyCursor()
        {
            bool shouldShowHammer = ActiveSources.Count > 0;
            if (shouldShowHammer == isHammerApplied)
            {
                return;
            }

            isHammerApplied = shouldShowHammer;
            if (!shouldShowHammer)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                return;
            }

            if (hammerCursor == null)
            {
                hammerCursor = Resources.Load<Texture2D>(
                    HammerCursorResourcePath);
                if (hammerCursor == null)
                {
                    hammerCursor = CreateHammerCursor();
                }
            }

            Cursor.SetCursor(hammerCursor, HammerHotspot, CursorMode.Auto);
        }

        private static Texture2D CreateHammerCursor()
        {
            var texture = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false, false)
            {
                name = "Runtime Build Hammer Cursor",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[CursorSize * CursorSize];
            Color32 outline = new Color32(32, 35, 40, 255);
            Color32 steel = new Color32(205, 215, 224, 255);
            Color32 steelHighlight = new Color32(242, 247, 250, 255);
            Color32 handle = new Color32(176, 105, 54, 255);
            Color32 handleHighlight = new Color32(224, 151, 87, 255);

            DrawThickLine(pixels, 13, 21, 25, 5, 6, outline);
            DrawThickLine(pixels, 13, 21, 25, 5, 4, handle);
            DrawThickLine(pixels, 13, 21, 24, 7, 1, handleHighlight);

            FillRect(pixels, 2, 21, 18, 9, outline);
            FillRect(pixels, 3, 22, 16, 7, steel);
            FillRect(pixels, 4, 27, 14, 1, steelHighlight);
            FillRect(pixels, 7, 19, 7, 3, outline);
            FillRect(pixels, 8, 20, 5, 2, handle);

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void FillRect(Color32[] pixels, int x, int y, int width, int height, Color32 color)
        {
            for (int py = y; py < y + height; py++)
            {
                for (int px = x; px < x + width; px++)
                {
                    SetPixel(pixels, px, py, color);
                }
            }
        }

        private static void DrawThickLine(
            Color32[] pixels,
            int startX,
            int startY,
            int endX,
            int endY,
            int thickness,
            Color32 color)
        {
            int dx = Mathf.Abs(endX - startX);
            int dy = Mathf.Abs(endY - startY);
            int sx = startX < endX ? 1 : -1;
            int sy = startY < endY ? 1 : -1;
            int error = dx - dy;
            int radius = thickness / 2;

            while (true)
            {
                FillRect(pixels, startX - radius, startY - radius, thickness, thickness, color);
                if (startX == endX && startY == endY)
                {
                    break;
                }

                int doubledError = error * 2;
                if (doubledError > -dy)
                {
                    error -= dy;
                    startX += sx;
                }

                if (doubledError < dx)
                {
                    error += dx;
                    startY += sy;
                }
            }
        }

        private static void SetPixel(Color32[] pixels, int x, int y, Color32 color)
        {
            if (x < 0 || x >= CursorSize || y < 0 || y >= CursorSize)
            {
                return;
            }

            pixels[y * CursorSize + x] = color;
        }
    }
}
