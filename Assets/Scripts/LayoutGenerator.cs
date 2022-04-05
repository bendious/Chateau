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
			Entryway,
			Zone1,

			Entrance,
			Zone1Door,
			RoomVertical,
			RoomHorizontal,
			Npc,
			Key,
			Lock,
			Secret,
			BonusItems,
			Boss,
			TightCoupling,

			BasementOrTower,
			Corridor,
			Sequence,
			SequenceMedium,
			SequenceLarge,
			GateLock,
			Gate,
			PossibleBonus
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
			return DirectParentsInternal.First().TightCoupleParent; // TODO: don't assume that all branches will have the same tight coupling parent?
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

		public int Depth { get
		{
			Node parent = TightCoupleParent;
			return parent == null ? 0 : parent.Depth + 1;
		} }

		public readonly Color m_color = Utility.ColorRandom(0.25f, 0.5f, 0.125f); // TODO: tend brighter based on progress?


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


		internal bool HasDescendant(Node node)
		{
			if (m_children == null)
			{
				return false;
			}
			if (m_children.Contains(node))
			{
				return true;
			}
			foreach (Node child in m_children)
			{
				if (child.HasDescendant(node))
				{
					return true;
				}
			}
			return false;
		}

		internal void DepthFirstQueue(Queue<Node> queue)
		{
			if (m_children != null)
			{
				foreach (Node child in m_children)
				{
					child.DepthFirstQueue(queue);
				}
			}

			if (!queue.Contains(this)) // NOTE that this check is necessary due to multi-parenting
			{
				queue.Enqueue(this);
			}
		}

		internal Node Clone()
		{
			return new Node(m_type, m_children?.Select(node => node.Clone()).ToList());
		}

		internal void AddChildren(List<Node> children)
		{
			if (m_children == null)
			{
				m_children = new(children); // NOTE the manual copy to prevent nodes sharing edits to each others' m_children
			}
			else
			{
				Assert.IsTrue(m_children.Intersect(children).Count() == 0);
				m_children.AddRange(children);
			}

			foreach (Node child in children)
			{
				if (child.DirectParentsInternal == null)
				{
					child.DirectParentsInternal = new();
				}
				Assert.IsTrue(!child.DirectParentsInternal.Contains(this));
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
		new(Node.Type.Entryway, new() { new(Node.Type.Entrance, new() { new(Node.Type.Corridor, new() { new(Node.Type.Zone1Door) }), new(Node.Type.Corridor, new() { new(Node.Type.Npc) }), new(Node.Type.BasementOrTower, new() { new(Node.Type.GateLock, new() { new(Node.Type.RoomVertical), new(Node.Type.BonusItems) }) }), new(Node.Type.BasementOrTower, new() { new(Node.Type.GateLock, new() { new(Node.Type.RoomVertical), new(Node.Type.BonusItems) }) }) }) }),
		new(Node.Type.Zone1, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.GateLock, new() { new(Node.Type.SequenceMedium, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.SequenceLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.Boss) }) }) }) }) }) }) }) }),

		new(Node.Type.BasementOrTower, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical) }) }) }) }) }) }),
		new(Node.Type.BasementOrTower, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical) }) }) }) }) }) }) }) }),
		new(Node.Type.Corridor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal) }) }) }) }) }) }),
		new(Node.Type.Corridor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal) }) }) }) }) }) }) }) }),

		new(Node.Type.SequenceMedium, new() { new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceLarge, new() { new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceLarge, new() { new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),

		// serial chains
		// NOTE that the leaf Keys are required for the following Locks
		new(Node.Type.Sequence, new() { new(Node.Type.Gate, new() { new(Node.Type.Key), new(Node.Type.PossibleBonus) }) }),
		new(Node.Type.PossibleBonus, null, 2.0f),
		new(Node.Type.PossibleBonus, new() { new(Node.Type.Gate, new() { new(Node.Type.BonusItems) }) }),

		// gate types
		new(Node.Type.GateLock, new() { new(Node.Type.Lock, new() { new(Node.Type.TightCoupling) }) }),
		new(Node.Type.Gate, new() { new(Node.Type.Secret, new() { new(Node.Type.TightCoupling) }) }, 0.5f),
		new(Node.Type.Gate, new() { new(Node.Type.Key, new() { new(Node.Type.GateLock) }) }),
	};


	private /*readonly*/ Node m_rootNode;


	public LayoutGenerator(Node rootNode)
	{
		m_rootNode = rootNode;
	}

	public void Generate()
	{
		Queue<Node> nodeQueue = new(new List<Node> { m_rootNode });

		while (nodeQueue.TryDequeue(out Node nodeItr))
		{
			if (nodeItr.m_processed)
			{
				continue;
			}
			nodeItr.m_processed = true;

			ReplacementRule[] options = m_rules.Where(rule => rule.m_initial == nodeItr.m_type).ToArray();

			if (options.Length == 0)
			{
				// next node
				if (nodeItr.m_children != null)
				{
					foreach (Node child in nodeItr.m_children)
					{
						nodeQueue.Enqueue(child);
					}
				}
				continue;
			}

			// replace node
			ReplacementRule replacement = options.RandomWeighted(options.Select(rule => rule.m_weight).ToArray());

			// move children
			List<Node> replacementNodes = replacement.m_final == null ? nodeItr.m_children.Where(childNode => !nodeItr.DirectParentsInternal.Any(parentNode => parentNode.HasDescendant(childNode))).ToList() : replacement.m_final.Select(node => node.Clone()).ToList();
			if (nodeItr.m_children != null)
			{
				foreach (Node child in nodeItr.m_children)
				{
					child.DirectParentsInternal.Remove(nodeItr);
				}
				foreach (Node replacementNode in replacementNodes)
				{
					replacementNode.AppendLeafChildren(nodeItr.m_children);
				}
			}

			// hook in replacement tree
			if (nodeItr.DirectParentsInternal == null)
			{
				Assert.IsTrue(replacementNodes.Count() == 1);
				m_rootNode = replacementNodes.First();
			}
			else
			{
				foreach (Node parent in nodeItr.DirectParentsInternal)
				{
					parent.m_children.Remove(nodeItr);
					parent.AddChildren(replacementNodes);
				}

				// preserve any existing multi-parenting
				foreach (Node replacementNode in replacementNodes)
				{
					replacementNode.DirectParentsInternal = replacementNode.DirectParentsInternal.Union(nodeItr.DirectParentsInternal).ToList();
				}
			}

			// iterate
			foreach (Node newNode in replacementNodes)
			{
				Assert.IsFalse(newNode.m_processed);
				nodeQueue.Enqueue(newNode);
			}
		}
	}

	public bool ForEachNodeDepthFirst(Func<Node, bool> f)
	{
		Queue<Node> queue = new();
		m_rootNode.DepthFirstQueue(queue);

		while (queue.TryDequeue(out Node next))
		{
			if (queue.Contains(next.TightCoupleParent))
			{
				// haven't processed our parent yet; try again later
				queue.Enqueue(next);
				continue;
			}

			bool done = next.m_type != Node.Type.TightCoupling && f(next); // NOTE the deliberate skip of internal-only nodes
			if (done)
			{
				return true;
			}
		}

		return false;
	}
}
