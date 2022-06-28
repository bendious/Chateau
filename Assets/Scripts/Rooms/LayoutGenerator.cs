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
			Entryway,
			Zone1,
			Tutorial,
			Zone2,
			Zone3,

			Entrance,
			TutorialMove,
			TutorialAim,
			TutorialDrop,
			TutorialJump,
			TutorialInteract,
			TutorialUse,
			TutorialSwap,
			TutorialThrow,
			TutorialInventory,
			TutorialSwing,
			TutorialCancel,
			TutorialCatch,
			ExitDoor,
			Room,
			RoomDown,
			RoomUp,
			RoomVertical,
			RoomHorizontal,
			Npc,
			Key,
			Lock,
			Secret,
			BonusItems,
			Boss,
			TightCoupling,
			RootCoupling, // TODO: replace w/ most-recent-common-ancestor logic?

			BasementOrTower,
			Corridor,
			Sequence,
			SequenceMedium,
			SequenceLarge,
			SequenceExtraLarge,
			GateLock,
			Gate,
			PossibleBonus
		}


		public readonly Type m_type;
		public List<Node> m_children;

		public RoomController m_room = null;


		public List<Node> DirectParents => DirectParentsInternal?.SelectMany(node => node.m_type == Type.TightCoupling || node.m_type == Type.RootCoupling ? node.DirectParents : new List<Node> { node }).ToList();

		public Node TightCoupleParent { get
		{
			if (DirectParentsInternal == null)
			{
				return null;
			}
			if (m_type == Type.RootCoupling)
			{
				return Root;
			}
			Node parentCoupling = DirectParentsInternal.FirstOrDefault(node => node.m_type == Type.TightCoupling);
			if (parentCoupling != null)
			{
				UnityEngine.Debug.Assert(parentCoupling.DirectParentsInternal.Count == 1);
				return parentCoupling.m_children.First() != this ? parentCoupling.m_children.First() : parentCoupling.DirectParentsInternal.First();
			}
			return DirectParentsInternal.First().TightCoupleParent; // TODO: don't assume that all branches will have the same tight coupling parent? find the most recent common ancestor?
		} }

		// NOTE that this relies upon the structure of layouts having multiple keys as the parents of a single lock at the end of each "area"
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
			return tcParentDirectParents.Count > 1 ? this : tightCoupleParent.AreaParent;
		} }

		public int Depth { get
		{
			List<Node> parents = DirectParents;
			return parents == null ? 0 : parents.Max(node => node.Depth) + 1;
		} }


		internal List<Node> DirectParentsInternal;

		internal bool m_processed = false;


		private Node Root => DirectParentsInternal == null ? this : DirectParentsInternal.First().Root; // TODO: don't assume unique roots?


		public Node(Type type, List<Node> children = null)
		{
			m_type = type;
			if (children != null)
			{
				AddChildren(children);
			}
		}

		public static List<Node> Multiparents(List<Node> parents, Node child)
		{
			child.DirectParentsInternal = null;
			List<Node> childList = new() { child };
			foreach (Node parent in parents)
			{
				parent.AppendLeafChildren(childList);
			}
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
			if (queue.Contains(this)) // NOTE that this check is necessary due to multi-parenting
			{
				return;
			}
			queue.Enqueue(this);

			if (m_children != null)
			{
				foreach (Node child in m_children)
				{
					child.DepthFirstQueue(queue);
				}
			}
		}

		internal Node Clone(List<Node> descendantsExcluded = null)
		{
			// determine full list of downstream nodes to handle/ignore
			List<Node> multiparentDescendants = MultiparentDescendants;
			if (multiparentDescendants != null)
			{
				if (descendantsExcluded == null)
				{
					descendantsExcluded = multiparentDescendants;
				}
				else
				{
					descendantsExcluded.AddRange(multiparentDescendants);
				}
			}

			// duplicate (w/ pruning of handled/ignored nodes) child trees
			List<Node> childrenDuplicated = m_children?.Where(child => descendantsExcluded == null || !descendantsExcluded.Exists(excluded => child.Equivalent(excluded)))?.Select(node => node.Clone(descendantsExcluded))?.ToList();
			if (childrenDuplicated != null && childrenDuplicated.Count <= 0)
			{
				childrenDuplicated = null;
			}

			// return copy of self w/ duplicate children, merging in multiparent descendants appropriately
			UnityEngine.Debug.Assert(multiparentDescendants == null || multiparentDescendants.Count() <= 1); // TODO: handle multiple multi-parent descendants?
			return new Node(m_type, multiparentDescendants != null && multiparentDescendants.Count() > 0 ? Multiparents(childrenDuplicated, multiparentDescendants.First()) : childrenDuplicated);
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
			if (m_children == null || m_children.Count <= 0)
			{
				AddChildren(children);
				return;
			}

			foreach (Node child in m_children)
			{
				child.AppendLeafChildren(children);
			}
		}


		private IEnumerable<Node> DescendantsRaw
		{
			get
			{
				if (m_children == null)
				{
					return null;
				}
				return m_children.Aggregate((IEnumerable<Node>)m_children, (prevList, node) =>
				{
					IEnumerable<Node> nodeDescendants = node.DescendantsRaw;
					return nodeDescendants == null ? prevList : prevList.Concat(nodeDescendants);
				});
			}
		}

		private List<Node> MultiparentDescendants // TODO: efficiency?
		{
			get
			{
				// collect descendants that are visited multiple times when looking down the tree
				IEnumerable<Node> descendants = DescendantsRaw;
				IEnumerable<Node> descendantsMulti = descendants?.Where(n => descendants.Count(n2 => n == n2) > 1)?.Distinct();
				if (descendantsMulti == null)
				{
					return null;
				}

				// remove redundant nodes already descendants of other held nodes
				List<Node> descendantsMultiParents = new();
				foreach (Node n in descendantsMulti)
				{
					bool isRedundant = false;
					foreach (Node n2 in descendantsMulti)
					{
						if (n == n2)
						{
							continue;
						}
						if (n2.HasDescendant(n))
						{
							isRedundant = true;
							break;
						}
					}
					if (!isRedundant)
					{
						descendantsMultiParents.Add(n);
					}
				}
				return descendantsMultiParents;
			}
		}

		// this compares nodes regardless of whether they are pre- or post-replacement
		private bool Equivalent(Node rhs)
		{
			if (this == rhs)
			{
				return true;
			}
			if (m_type != rhs.m_type)
			{
				return false;
			}
			if (m_children == null && rhs.m_children == null)
			{
				return true;
			}
			if (m_children == null || rhs.m_children == null || m_children.Count != rhs.m_children.Count) // TODO: allow extra children on one side as long as all existing children match?
			{
				return false;
			}
			for (int i = 0; i < m_children.Count && i < rhs.m_children.Count; ++i)
			{
				if (!m_children[i].Equivalent(rhs.m_children[i])) // TODO: don't require matching child order?
				{
					return false;
				}
			}
			return true;
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
		new(Node.Type.Tutorial, new() { new(Node.Type.Entrance, new() { new(Node.Type.TutorialMove), new(Node.Type.TutorialAim), new(Node.Type.TutorialDrop, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomDown, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.TutorialJump, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomUp, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.TutorialInteract), new(Node.Type.TutorialUse), new(Node.Type.TutorialSwap), new(Node.Type.TutorialThrow), new(Node.Type.Secret, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.ExitDoor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.TutorialInventory), new(Node.Type.TutorialSwing), new(Node.Type.TutorialCancel), new(Node.Type.TutorialCatch) }) }) }) }) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Entryway, new() { new(Node.Type.Entrance, Node.Multiparents(new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.ExitDoor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.ExitDoor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.ExitDoor) }) }) }) }) }) }) }) }) }) }) }), new(Node.Type.BasementOrTower, new() { new(Node.Type.GateLock, new() { new(Node.Type.RoomVertical) }) }), new(Node.Type.BasementOrTower, new() { new(Node.Type.GateLock, new() { new(Node.Type.RoomVertical) }) }), new(Node.Type.Corridor) }, new(Node.Type.RootCoupling, new() { new(Node.Type.Npc), new(Node.Type.Npc), new(Node.Type.Npc), new(Node.Type.Npc) }))) }),
		new(Node.Type.Zone1, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.GateLock, new() { new(Node.Type.SequenceMedium, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.SequenceLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.Boss, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Npc), new(Node.Type.ExitDoor), new(Node.Type.ExitDoor) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Zone2, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.GateLock, new() { new(Node.Type.SequenceLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.SequenceExtraLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.Boss, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Npc), new(Node.Type.ExitDoor), new(Node.Type.ExitDoor) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Zone3, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.GateLock, new() { new(Node.Type.SequenceMedium, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.SequenceLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.SequenceExtraLarge, new() { new Node(Node.Type.GateLock, new() { new(Node.Type.Boss, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Npc), new(Node.Type.ExitDoor) }) }) }) }) }) }) }) }) }) }) }) }) }) }),

		new(Node.Type.BasementOrTower, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical) }) }) }) }) }) }),
		new(Node.Type.BasementOrTower, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical) }) }) }) }) }) }) }) }),
		new(Node.Type.Corridor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal) }) }) }) }) }) }),
		new(Node.Type.Corridor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal) }) }) }) }) }) }) }) }),

		new(Node.Type.SequenceMedium, new() { new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceLarge, new() { new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceExtraLarge, new() { new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),

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
			if (next.DirectParents != null && Array.Exists(queue.ToArray(), queueNode => next.DirectParents.Contains(queueNode))) // efficiency?
			{
				// haven't processed all our parents yet; try again later
				queue.Enqueue(next);
				continue;
			}

			bool done = next.m_type != Node.Type.TightCoupling && next.m_type != Node.Type.RootCoupling && f(next); // NOTE the deliberate skip of internal-only nodes
			if (done)
			{
				return true;
			}
		}

		return false;
	}
}
