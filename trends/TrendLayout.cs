using System.Collections.Generic;

namespace ArdisCVDCore.trends
{
    public enum TrendArrange
    {
        Vertical,
        Horizontal,
        Grid
    }

    internal sealed class LayoutNode
    {
        public string Key;
        public bool Vertical;
        public float Weight = 1f;
        public List<LayoutNode> Children = new List<LayoutNode>();

        public bool IsLeaf
        {
            get { return Key != null; }
        }
    }

    internal sealed class TrendLayout
    {
        public LayoutNode Root;

        public static TrendArrange Next(TrendArrange arrange, int count)
        {
            if (count <= 2)
                return arrange == TrendArrange.Horizontal ? TrendArrange.Vertical : TrendArrange.Horizontal;

            switch (arrange)
            {
                case TrendArrange.Vertical:
                    return TrendArrange.Horizontal;
                case TrendArrange.Horizontal:
                    return TrendArrange.Grid;
                default:
                    return TrendArrange.Vertical;
            }
        }

        public static TrendLayout Build(IList<string> keys, TrendArrange arrange, bool loneFirst)
        {
            if (arrange == TrendArrange.Grid && keys.Count > 2)
                return Grid(keys, loneFirst && keys.Count % 2 == 1);
            return Line(keys, arrange != TrendArrange.Horizontal);
        }

        private static LayoutNode Leaf(string key)
        {
            return new LayoutNode { Key = key };
        }

        private static LayoutNode Split(bool vertical, IList<LayoutNode> children)
        {
            if (children.Count == 1)
                return children[0];

            LayoutNode split = new LayoutNode { Vertical = vertical };
            split.Children.AddRange(children);
            return split;
        }

        private static TrendLayout Line(IList<string> keys, bool vertical)
        {
            TrendLayout layout = new TrendLayout();
            if (keys.Count == 0)
                return layout;

            List<LayoutNode> leaves = new List<LayoutNode>();
            foreach (string key in keys)
                leaves.Add(Leaf(key));

            layout.Root = Split(vertical, leaves);
            return layout;
        }

        private static TrendLayout Grid(IList<string> keys, bool loneFirst)
        {
            List<LayoutNode> rows = new List<LayoutNode>();
            int i = 0;
            if (loneFirst)
                rows.Add(Leaf(keys[i++]));
            for (; i < keys.Count; i += 2)
            {
                List<LayoutNode> row = new List<LayoutNode> { Leaf(keys[i]) };
                if (i + 1 < keys.Count)
                    row.Add(Leaf(keys[i + 1]));
                rows.Add(Split(false, row));
            }

            return new TrendLayout { Root = Split(true, rows) };
        }

        public TrendLayout Clone()
        {
            return new TrendLayout { Root = Copy(Root) };
        }

        private static LayoutNode Copy(LayoutNode node)
        {
            if (node == null)
                return null;

            LayoutNode copy = new LayoutNode { Key = node.Key, Vertical = node.Vertical, Weight = node.Weight };
            foreach (LayoutNode child in node.Children)
                copy.Children.Add(Copy(child));
            return copy;
        }

        public List<string> Keys()
        {
            List<string> keys = new List<string>();
            CollectKeys(Root, keys);
            return keys;
        }

        private static void CollectKeys(LayoutNode node, List<string> keys)
        {
            if (node == null)
                return;
            if (node.IsLeaf)
            {
                keys.Add(node.Key);
                return;
            }
            foreach (LayoutNode child in node.Children)
                CollectKeys(child, keys);
        }

        public List<string> ReadingOrder()
        {
            if (IsQuad && !Root.Vertical)
                return new List<string>
                {
                    Root.Children[0].Children[0].Key,
                    Root.Children[1].Children[0].Key,
                    Root.Children[0].Children[1].Key,
                    Root.Children[1].Children[1].Key
                };
            return Keys();
        }

        public LayoutNode Find(string key)
        {
            LayoutNode parent;
            return Find(Root, null, key, out parent);
        }

        public LayoutNode ParentOf(string key)
        {
            LayoutNode parent;
            Find(Root, null, key, out parent);
            return parent;
        }

        private static LayoutNode Find(LayoutNode node, LayoutNode owner, string key, out LayoutNode parent)
        {
            parent = null;
            if (node == null)
                return null;
            if (node.IsLeaf)
            {
                if (node.Key != key)
                    return null;
                parent = owner;
                return node;
            }
            foreach (LayoutNode child in node.Children)
            {
                LayoutNode found = Find(child, node, key, out parent);
                if (found != null)
                    return found;
            }
            return null;
        }

        public void Swap(string first, string second)
        {
            LayoutNode a = Find(first);
            LayoutNode b = Find(second);
            if (a == null || b == null)
                return;

            a.Key = second;
            b.Key = first;

            if (ParentOf(first) == ParentOf(second))
            {
                float weight = a.Weight;
                a.Weight = b.Weight;
                b.Weight = weight;
            }
        }

        public bool IsLone(string key)
        {
            return Root != null && !Root.IsLeaf && Root.Children.Count == 2
                && Root.Children.Exists(child => child.IsLeaf && child.Key == key)
                && Root.Children.Exists(child => !child.IsLeaf);
        }

        public void FlipRows()
        {
            if (Root != null && !Root.IsLeaf)
                Root.Children.Reverse();
        }

        public bool IsQuad
        {
            get
            {
                if (Root == null || Root.IsLeaf || Root.Children.Count != 2)
                    return false;
                foreach (LayoutNode child in Root.Children)
                {
                    if (child.IsLeaf || child.Vertical == Root.Vertical || child.Children.Count != 2)
                        return false;
                    if (!child.Children[0].IsLeaf || !child.Children[1].IsLeaf)
                        return false;
                }
                return true;
            }
        }

        public void Transpose()
        {
            if (!IsQuad)
                return;

            LayoutNode first = Root.Children[0];
            LayoutNode second = Root.Children[1];
            float total = first.Children[0].Weight + first.Children[1].Weight;

            LayoutNode root = new LayoutNode { Vertical = !Root.Vertical, Weight = Root.Weight };
            for (int j = 0; j < 2; j++)
            {
                LayoutNode a = first.Children[j];
                LayoutNode b = second.Children[j];
                LayoutNode line = new LayoutNode
                {
                    Vertical = Root.Vertical,
                    Weight = total > 0f ? a.Weight / total : 0.5f
                };
                line.Children.Add(new LayoutNode { Key = a.Key, Weight = first.Weight });
                line.Children.Add(new LayoutNode { Key = b.Key, Weight = second.Weight });
                root.Children.Add(line);
            }
            Root = root;
        }

        public void AlignWith(LayoutNode line)
        {
            if (!IsQuad || !Root.Children.Contains(line))
                return;

            LayoutNode other = Root.Children[0] == line ? Root.Children[1] : Root.Children[0];
            other.Children[0].Weight = line.Children[0].Weight;
            other.Children[1].Weight = line.Children[1].Weight;
        }
    }
}
