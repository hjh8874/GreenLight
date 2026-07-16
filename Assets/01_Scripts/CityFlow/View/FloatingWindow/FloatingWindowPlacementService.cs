using System.Collections.Generic;
using UnityEngine;

namespace CityFlow.View
{
    public enum FloatingWindowAnchor
    {
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
    }

    public readonly struct FloatingWindowPlacement
    {
        public Vector2 Position { get; }
        public Vector2 Size { get; }
        public WindowWorkArea WorkArea { get; }
        public FloatingWindowAnchor Anchor { get; }

        public FloatingWindowPlacement(
            Vector2 position,
            Vector2 size,
            WindowWorkArea workArea,
            FloatingWindowAnchor anchor)
        {
            Position = position;
            Size = size;
            WorkArea = workArea;
            Anchor = anchor;
        }
    }

    public sealed class FloatingWindowPlacementService
    {
        private readonly IWindowWorkAreaProvider workAreaProvider;
        private readonly float edgeMargin;
        private readonly float cornerSnapDistance;

        public FloatingWindowPlacementService(
            IWindowWorkAreaProvider workAreaProvider,
            float edgeMargin = 0f,
            float cornerSnapDistance = 36f)
        {
            this.workAreaProvider = workAreaProvider;
            this.edgeMargin = Mathf.Max(0f, edgeMargin);
            this.cornerSnapDistance = Mathf.Max(0f, cornerSnapDistance);
        }

        public WindowWorkArea SelectWorkArea(
            Vector2 windowPosition,
            Vector2 windowSize,
            Vector2 cursorPosition)
        {
            IReadOnlyList<WindowWorkArea> workAreas = workAreaProvider.GetWorkAreas();
            Rect windowBounds = new Rect(windowPosition, windowSize);
            int bestIndex = 0;
            float bestOverlap = -1f;

            for (int i = 0; i < workAreas.Count; i++)
            {
                float overlap = GetIntersectionArea(windowBounds, workAreas[i].Bounds);
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    bestIndex = i;
                }
            }

            if (bestOverlap > 0f)
            {
                return workAreas[bestIndex];
            }

            for (int i = 0; i < workAreas.Count; i++)
            {
                if (workAreas[i].Bounds.Contains(cursorPosition))
                {
                    return workAreas[i];
                }
            }

            float nearestDistance = float.MaxValue;
            Vector2 referencePoint = windowBounds.center;
            for (int i = 0; i < workAreas.Count; i++)
            {
                Rect bounds = workAreas[i].Bounds;
                Vector2 closestPoint = new Vector2(
                    Mathf.Clamp(referencePoint.x, bounds.xMin, bounds.xMax),
                    Mathf.Clamp(referencePoint.y, bounds.yMin, bounds.yMax));
                float distance = (referencePoint - closestPoint).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    bestIndex = i;
                }
            }

            return workAreas[bestIndex];
        }

        public Vector2 FitSizeToWorkArea(Vector2 requestedSize, WindowWorkArea workArea)
        {
            float availableWidth = Mathf.Max(1f, workArea.Bounds.width - edgeMargin * 2f);
            float availableHeight = Mathf.Max(1f, workArea.Bounds.height - edgeMargin * 2f);
            float width = Mathf.Max(1f, requestedSize.x);
            float height = Mathf.Max(1f, requestedSize.y);
            float scale = Mathf.Min(1f, availableWidth / width, availableHeight / height);

            return new Vector2(
                Mathf.Max(1f, Mathf.Floor(width * scale)),
                Mathf.Max(1f, Mathf.Floor(height * scale)));
        }

        public FloatingWindowPlacement PlaceAtBottomRight(
            Vector2 requestedSize,
            WindowWorkArea workArea)
        {
            Vector2 fittedSize = FitSizeToWorkArea(requestedSize, workArea);
            Vector2 position = new Vector2(
                workArea.Bounds.xMax - fittedSize.x - edgeMargin,
                workArea.Bounds.yMin + edgeMargin);

            return new FloatingWindowPlacement(
                position,
                fittedSize,
                workArea,
                FloatingWindowAnchor.BottomRight);
        }

        public FloatingWindowPlacement ResizeWithinWorkArea(
            Vector2 currentPosition,
            Vector2 currentSize,
            Vector2 requestedSize,
            Vector2 cursorPosition)
        {
            WindowWorkArea workArea = SelectWorkArea(
                currentPosition,
                currentSize,
                cursorPosition);
            FloatingWindowAnchor anchor = GetNearestAnchor(
                currentPosition,
                currentSize,
                workArea);
            Vector2 fittedSize = FitSizeToWorkArea(requestedSize, workArea);
            Vector2 position = PlaceUsingAnchor(
                currentPosition,
                fittedSize,
                workArea,
                anchor);

            return new FloatingWindowPlacement(position, fittedSize, workArea, anchor);
        }

        public FloatingWindowPlacement ConstrainAfterDrag(
            Vector2 currentPosition,
            Vector2 currentSize,
            Vector2 cursorPosition)
        {
            WindowWorkArea workArea = SelectWorkArea(
                currentPosition,
                currentSize,
                cursorPosition);
            Vector2 fittedSize = FitSizeToWorkArea(currentSize, workArea);
            Vector2 clampedPosition = ClampPosition(
                currentPosition,
                fittedSize,
                workArea);
            FloatingWindowAnchor anchor = GetNearestAnchor(
                clampedPosition,
                fittedSize,
                workArea);
            Vector2 snappedPosition = PlaceUsingAnchor(
                clampedPosition,
                fittedSize,
                workArea,
                anchor);

            return new FloatingWindowPlacement(
                snappedPosition,
                fittedSize,
                workArea,
                anchor);
        }

        private FloatingWindowAnchor GetNearestAnchor(
            Vector2 position,
            Vector2 size,
            WindowWorkArea workArea)
        {
            Rect bounds = workArea.Bounds;
            float minX = bounds.xMin + edgeMargin;
            float maxX = bounds.xMax - edgeMargin - size.x;
            float minY = bounds.yMin + edgeMargin;
            float maxY = bounds.yMax - edgeMargin - size.y;
            float left = Mathf.Abs(position.x - minX);
            float right = Mathf.Abs(maxX - position.x);
            float bottom = Mathf.Abs(position.y - minY);
            float top = Mathf.Abs(maxY - position.y);
            float horizontal = Mathf.Min(left, right);
            float vertical = Mathf.Min(bottom, top);

            if (horizontal <= cornerSnapDistance && vertical <= cornerSnapDistance)
            {
                bool useLeft = left <= right;
                bool useBottom = bottom <= top;

                if (useLeft && useBottom) return FloatingWindowAnchor.BottomLeft;
                if (useLeft) return FloatingWindowAnchor.TopLeft;
                if (useBottom) return FloatingWindowAnchor.BottomRight;
                return FloatingWindowAnchor.TopRight;
            }

            if (horizontal <= vertical)
            {
                return left <= right
                    ? FloatingWindowAnchor.Left
                    : FloatingWindowAnchor.Right;
            }

            return bottom <= top
                ? FloatingWindowAnchor.Bottom
                : FloatingWindowAnchor.Top;
        }

        private Vector2 PlaceUsingAnchor(
            Vector2 currentPosition,
            Vector2 size,
            WindowWorkArea workArea,
            FloatingWindowAnchor anchor)
        {
            Vector2 position = ClampPosition(currentPosition, size, workArea);
            Rect bounds = workArea.Bounds;
            float minX = bounds.xMin + edgeMargin;
            float maxX = Mathf.Max(minX, bounds.xMax - edgeMargin - size.x);
            float minY = bounds.yMin + edgeMargin;
            float maxY = Mathf.Max(minY, bounds.yMax - edgeMargin - size.y);

            switch (anchor)
            {
                case FloatingWindowAnchor.Left:
                    position.x = minX;
                    break;
                case FloatingWindowAnchor.Right:
                    position.x = maxX;
                    break;
                case FloatingWindowAnchor.Top:
                    position.y = maxY;
                    break;
                case FloatingWindowAnchor.Bottom:
                    position.y = minY;
                    break;
                case FloatingWindowAnchor.TopLeft:
                    position = new Vector2(minX, maxY);
                    break;
                case FloatingWindowAnchor.TopRight:
                    position = new Vector2(maxX, maxY);
                    break;
                case FloatingWindowAnchor.BottomLeft:
                    position = new Vector2(minX, minY);
                    break;
                case FloatingWindowAnchor.BottomRight:
                    position = new Vector2(maxX, minY);
                    break;
            }

            return position;
        }

        private Vector2 ClampPosition(
            Vector2 position,
            Vector2 size,
            WindowWorkArea workArea)
        {
            Rect bounds = workArea.Bounds;
            float minX = bounds.xMin + edgeMargin;
            float maxX = Mathf.Max(minX, bounds.xMax - edgeMargin - size.x);
            float minY = bounds.yMin + edgeMargin;
            float maxY = Mathf.Max(minY, bounds.yMax - edgeMargin - size.y);

            return new Vector2(
                Mathf.Clamp(position.x, minX, maxX),
                Mathf.Clamp(position.y, minY, maxY));
        }

        private static float GetIntersectionArea(Rect first, Rect second)
        {
            float width = Mathf.Max(0f, Mathf.Min(first.xMax, second.xMax) - Mathf.Max(first.xMin, second.xMin));
            float height = Mathf.Max(0f, Mathf.Min(first.yMax, second.yMax) - Mathf.Max(first.yMin, second.yMin));
            return width * height;
        }
    }
}
