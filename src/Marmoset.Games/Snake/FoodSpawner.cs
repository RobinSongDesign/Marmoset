using System;
using System.Collections.Generic;
using Marmoset.Games.Common;

namespace Marmoset.Games.Snake
{
    internal class FoodSpawner
    {
        private readonly Random _random;

        public FoodSpawner(int seed)
        {
            _random = seed == 0 ? new Random() : new Random(seed);
        }

        public GridPoint GetFoodPoint(int width, int height, IReadOnlyList<GridPoint> blockedCells)
        {
            if (width <= 0 || height <= 0)
                return new GridPoint(0, 0);

            for (int attempt = 0; attempt < 1000; attempt++)
            {
                var point = new GridPoint(_random.Next(0, width), _random.Next(0, height));
                if (!Contains(blockedCells, point))
                    return point;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var point = new GridPoint(x, y);
                    if (!Contains(blockedCells, point))
                        return point;
                }
            }

            return new GridPoint(0, 0);
        }

        private static bool Contains(IReadOnlyList<GridPoint> points, GridPoint target)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] == target)
                    return true;
            }

            return false;
        }
    }
}
