using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Assertions;


public class LayoutGenerator
{
	public class Node
	{
		public enum Type
		{
			Entrance,
			Key,
			Lock,
			Secret,
			Items,
			Boss,

			Initial,
			Sequence,
			SequenceParallel,
			SequenceSerial,
			Gate,
		}


		public readonly Type m_type;
		public List<Node> m_children;

		public RoomController m_room = null;


		public List<Node> DirectParents { get; internal set; }

		public Node TightCoupleParent { get
		{
			if (DirectParents == null)
			{
				return null;
			}
			Node parent = DirectParents.FirstOrDefault(node => node.m_type == Type.Lock || node.m_type == Type.Secret || node.DirectParents == null); // TODO: avoid all children of lock/secret nodes being tightly coupled?
			if (parent != null)
			{
				return parent;
			}
			return DirectParents.First().TightCoupleParent; // TODO: don't assume that all branches will have the same tight coupling parent?
		} }


		internal bool m_processed = false;


		public Node(Type type, List<Node> children = null)
		{
			m_type = type;
			if (children != null)
			{
				AddChildren(children);
			}
		}

		public static List<Node> Multiparents(List<Type> types, Node child)
		{
			List<Node> parents = new();
			List<Node> childList = new() { child };
			foreach (Type type in types)
			{
				parents.Add(new Node(type, childList));
			}
			child.DirectParents = parents;
			return parents;
		}

		public bool ForEach(Func<Node, bool> f, List<Node> processedNodes = null)
		{
			if (processedNodes == null)
			{
				processedNodes = new();
			}
			if (processedNodes.Contains(this))
			{
				return false;
			}

			bool done = f(this);
			processedNodes.Add(this);

			if (done || m_children == null)
			{
				return done;
			}

			foreach (Node child in m_children)
			{
				if (!child.DirectParents.All(parent => processedNodes.Contains(parent)))
				{
					// haven't processed all our parents yet; we'll try again later when the next parent comes around
					continue;
				}

				done = child.ForEach(f, processedNodes);
				if (done)
				{
					break;
				}
			}
			return done;
		}


		internal Node Clone()
		{
			return new Node(m_type, m_children?.Select(node => node.Clone()).ToList());
		}

		internal void AddChildren(List<Node> children)
		{
			if (m_children == null)
			{
				m_children = children;
			}
			else
			{
				m_children.AddRange(children);
			}

			foreach (Node child in children)
			{
				if (child.DirectParents == null)
				{
					child.DirectParents = new();
				}
				child.DirectParents.Add(this);
			}
		}

		internal void AppendLeafChildren(List<Node> children)
		{
			if (m_children == null)
			{
				AddChildren(children);
				return;
			}

			foreach (Node child in m_children)
			{
				child.AppendLeafChildren(children);
			}
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
		new(Node.Type.Initial, new() { new(Node.Type.Entrance, new() { new(Node.Type.Sequence, new() { new Node(Node.Type.Lock, new() { new(Node.Type.Boss) }) }) }) }),

		// parallel chains
		new(Node.Type.Sequence, new() { new(Node.Type.SequenceParallel) }),
		new(Node.Type.SequenceParallel, new() { new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial) }),
		new(Node.Type.SequenceParallel, new() { new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial) }),
		new(Node.Type.SequenceParallel, new() { new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial), new(Node.Type.SequenceSerial) }),

		// serial chains
		// TODO: allow parallel branches w/i serial chains w/o infinite recursion?
		new(Node.Type.SequenceSerial, new() { new(Node.Type.Gate, new() { new(Node.Type.Key) }) }),
		new(Node.Type.SequenceSerial, new() { new(Node.Type.Gate, new() { new(Node.Type.Gate, new() { new(Node.Type.Key) }) }) }),
		new(Node.Type.SequenceSerial, new() { new(Node.Type.Gate, new() { new(Node.Type.Gate, new() { new(Node.Type.Gate, new() { new(Node.Type.Key) }) }) }) }),

		// gate types
		new(Node.Type.Gate, new() { new(Node.Type.Secret, new() { new(Node.Type.Items) }) }),
		new(Node.Type.Gate, new() { new(Node.Type.Key, new() { new(Node.Type.Lock, new() { new(Node.Type.Items) }) }) }),
	};


	private /*readonly*/ Node m_rootNode = new(Node.Type.Initial);


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
			if (nodeAndParentItr.Item1.DirectParents != null && Enumerable.Intersect(nodeAndParentItr.Item1.DirectParents, nodeAndParentQueue.Select(pair => pair.Item1)).Count() > 0)
			{
				// haven't placed all our parents yet; try again later
				nodeAndParentQueue.Enqueue(nodeAndParentItr);
				continue;
			}

			ReplacementRule[] options = m_rules.Where(rule => rule.m_initial == nodeAndParentItr.Item1.m_type).ToArray();

			if (options.Length == 0)
			{
				// next node
				if (nodeAndParentItr.Item1.m_children != null)
				{
					foreach (Node child in nodeAndParentItr.Item1.m_children)
					{
						if (child.m_processed)
						{
							continue;
						}
						child.m_processed = true;
						nodeAndParentQueue.Enqueue(Tuple.Create(child, nodeAndParentItr.Item1));
					}
				}
				continue;
			}

			// replace node
			ReplacementRule replacement = Utility.RandomWeighted(options, options.Select(rule => rule.m_weight).ToArray());

			// move children
			List<Node> replacementNodes = replacement.m_final.Select(node => node.Clone()).ToList();
			if (nodeAndParentItr.Item1.m_children != null)
			{
				foreach (Node child in nodeAndParentItr.Item1.m_children)
				{
					child.DirectParents.Remove(nodeAndParentItr.Item1);
				}
				foreach (Node replacementNode in replacementNodes)
				{
					replacementNode.AppendLeafChildren(nodeAndParentItr.Item1.m_children);
				}
			}

			// hook in replacement tree
			if (nodeAndParentItr.Item2 == null)
			{
				Assert.IsTrue(replacementNodes.Count() == 1);
				m_rootNode = replacementNodes.First();
			}
			else
			{
				nodeAndParentItr.Item2.m_children.Remove(nodeAndParentItr.Item1);
				nodeAndParentItr.Item2.AddChildren(replacementNodes);

				// preserve any existing multi-parenting
				foreach (Node replacementNode in replacementNodes)
				{
					replacementNode.DirectParents = replacementNode.DirectParents.Union(nodeAndParentItr.Item1.DirectParents).ToList();
				}
			}

			// iterate
			foreach (Node newNode in replacementNodes)
			{
				nodeAndParentQueue.Enqueue(Tuple.Create(newNode, nodeAndParentItr.Item2));
			}
		}
	}

	public bool ForEachNode(Func<Node, bool> f)
	{
		return m_rootNode.ForEach(f);
	}
}
