using System;
using System.Collections.Generic;
using System.Linq;


public class LayoutGenerator
{
	public class Node
	{
		public enum Type
		{
			Entrance,
			Key,
			Lock,
			Boss,

			Sequence,
			SequenceParallel,
			SequenceSerial,
			KeyLockPair,
		}


		public Type m_type;
		public List<Node> m_children;


		public Node(Type type, List<Node> children = null)
		{
			m_type = type;
			m_children = children;
		}

		public void ForEach(Action<Node> f)
		{
			f(this);

			if (m_children == null)
			{
				return;
			}

			foreach (Node child in m_children)
			{
				child.ForEach(f);
			}
		}


		internal Node Clone()
		{
			return new Node(m_type, m_children?.Select(node => node.Clone()).ToList());
		}

		internal void AppendChildren(List<Node> children)
		{
			if (m_children == null)
			{
				m_children = children;
				return;
			}
			m_children[UnityEngine.Random.Range(0, m_children.Count())].AppendChildren(children);
		}
	}


	private struct ReplacementRule
	{
		public Node.Type m_initial; // TODO: convert to Node, to allow input chains?
		public List<Node> m_final;
		public float m_weight;

		public ReplacementRule(Node.Type initial, List<Node> final, float weight = 1.0f) { m_initial = initial; m_final = final; m_weight = weight; }
	}

	private static readonly ReplacementRule[] m_rules =
	{
		// parallel chains
		new(Node.Type.Sequence, new List<Node> { new(Node.Type.SequenceParallel) }),
		new(Node.Type.SequenceParallel, new List<Node> { new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial) }),
		new(Node.Type.SequenceParallel, new List<Node> { new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial) }),

		// serial chains
		// TODO: allow parallel branches w/i serial chains w/o infinite recursion?
		new(Node.Type.SequenceSerial, new List<Node> { new(Node.Type.KeyLockPair) }),
		new(Node.Type.SequenceSerial, new List<Node> { new(Node.Type.KeyLockPair, new List<Node> { new(Node.Type.KeyLockPair) }) }),
		new(Node.Type.SequenceSerial, new List<Node> { new(Node.Type.KeyLockPair, new List<Node> { new(Node.Type.KeyLockPair, new List<Node> { new(Node.Type.KeyLockPair) }) }) }),

		// keys/locks
		new(Node.Type.KeyLockPair, new List<Node> { new(Node.Type.Key, new List<Node> { new(Node.Type.Lock) }) }),
	};


	private readonly Node m_rootNode = new(Node.Type.Entrance, new() { new(Node.Type.Sequence, new() { new Node(Node.Type.Boss) }) });


	public LayoutGenerator()
	{
	}

	public LayoutGenerator(Node rootNode)
	{
		m_rootNode = rootNode;
	}

	public void Generate()
	{
		Queue<Tuple<Node, Node>> nodeAndParentQueue = new(new List<Tuple<Node, Node>> { new(m_rootNode, null) });

		while (nodeAndParentQueue.TryDequeue(out Tuple<Node, Node> nodeAndParentItr))
		{
			ReplacementRule[] options = m_rules.Where(rule => rule.m_initial == nodeAndParentItr.Item1.m_type).ToArray();

			if (options.Length == 0)
			{
				// next node
				if (nodeAndParentItr.Item1.m_children != null)
				{
					foreach (Node child in nodeAndParentItr.Item1.m_children)
					{
						nodeAndParentQueue.Enqueue(Tuple.Create(child, nodeAndParentItr.Item1));
					}
				}
				continue;
			}

			// replace node
			ReplacementRule replacement = Utility.RandomWeighted(options, options.Select(rule => rule.m_weight).ToArray());

			List<Node> replacementNodes = replacement.m_final.Select(node => node.Clone()).ToList();
			replacementNodes[UnityEngine.Random.Range(0, replacementNodes.Count())].AppendChildren(nodeAndParentItr.Item1.m_children);

			UnityEngine.Assertions.Assert.IsNotNull(nodeAndParentItr.Item2); // NOTE that the first node should always be Entrance, which should never be replaced
			nodeAndParentItr.Item2.m_children.Remove(nodeAndParentItr.Item1);
			nodeAndParentItr.Item2.m_children.AddRange(replacementNodes);

			foreach (Node newNode in replacementNodes)
			{
				nodeAndParentQueue.Enqueue(Tuple.Create(newNode, nodeAndParentItr.Item2));
			}
		}
	}

	public void ForEachNode(Action<Node> f)
	{
		m_rootNode.ForEach(f);
	}
}
