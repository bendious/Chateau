using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
			BonusItems,
			Boss,
			TightCoupling,

			Initial,
			Sequence,
			SequenceIntro,
			SequenceMedium,
			SequenceLarge,
			GateLock,
			Gate,
		}


		public readonly Type m_type;
		public List<Node> m_children;

		public RoomController m_room = null;


		public List<Node> DirectParents => DirectParentsInternal?.SelectMany(node => node.m_type == Type.TightCoupling ? node.DirectParents : new List<Node> { node }).ToList();

		public Node TightCoupleParent { get
		{
			if (DirectParentsInternal == null)
			{
				return null;
			}
			Node parentCoupling = DirectParentsInternal.FirstOrDefault(node => node.m_type == Type.TightCoupling);
			if (parentCoupling != null)
			{
				Assert.IsTrue(parentCoupling.DirectParentsInternal.Count == 1);
				return parentCoupling.m_children.First() != this ? parentCoupling.m_children.First() : parentCoupling.DirectParentsInternal.First();
			}
			Node firstParent = DirectParentsInternal.First(); // TODO: don't assume that all branches will have the same tight coupling parent?
			Node firstParentTight = firstParent.TightCoupleParent;
			return firstParentTight ?? firstParent;
		} }

		public Node AreaParent { get
		{
			Node tightCoupleParent = TightCoupleParent;
			if (tightCoupleParent == null)
			{
				return this;
			}
			List<Node> tcParentDirectParents = tightCoupleParent.DirectParentsInternal;
			if (tcParentDirectParents == null)
			{
				return tightCoupleParent;
			}
			return tcParentDirectParents.Count > 1 ? tightCoupleParent : tightCoupleParent.AreaParent;
		} }

		public readonly Color m_color = new(UnityEngine.Random.Range(0.25f, 0.5f), UnityEngine.Random.Range(0.25f, 0.5f), UnityEngine.Random.Range(0.25f, 0.5f)); // TODO: tend brighter based on progress?


		internal List<Node> DirectParentsInternal;

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
			child.DirectParentsInternal = parents;
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

			bool done = m_type != Type.TightCoupling && f(this); // NOTE the deliberate skip of internal-only nodes
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
				if (child.DirectParentsInternal == null)
				{
					child.DirectParentsInternal = new();
				}
				child.DirectParentsInternal.Add(this);
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
		new(Node.Type.Initial, new() { new(Node.Type.Entrance, new() { new(Node.Type.SequenceIntro, new() { new(Node.Type.GateLock, new() { new(Node.Type.SequenceMedium, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.SequenceLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.Boss) }) }) }) }) }) }) }) }),

		new(Node.Type.SequenceIntro, new() { new(Node.Type.BonusItems), new(Node.Type.Key) }),
		new(Node.Type.SequenceMedium, new() { new(Node.Type.BonusItems), new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceLarge, new() { new(Node.Type.BonusItems), new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceLarge, new() { new(Node.Type.BonusItems), new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),

		// serial chains
		// NOTE that the leaf Keys are required for the following Locks
		new(Node.Type.Sequence, new() { new(Node.Type.Gate, new() { new(Node.Type.Key) }) }),

		// gate types
		new(Node.Type.GateLock, new() { new(Node.Type.Lock, new() { new(Node.Type.TightCoupling) }) }),
		new(Node.Type.Gate, new() { new(Node.Type.Secret, new() { new(Node.Type.TightCoupling) }) }, 0.5f),
		new(Node.Type.Gate, new() { new(Node.Type.Key, new() { new(Node.Type.GateLock) }) }),
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
			if (nodeAndParentItr.Item1.DirectParentsInternal != null && Enumerable.Intersect(nodeAndParentItr.Item1.DirectParentsInternal, nodeAndParentQueue.Select(pair => pair.Item1)).Count() > 0)
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
					child.DirectParentsInternal.Remove(nodeAndParentItr.Item1);
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
					replacementNode.DirectParentsInternal = replacementNode.DirectParentsInternal.Union(nodeAndParentItr.Item1.DirectParentsInternal).ToList();
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
