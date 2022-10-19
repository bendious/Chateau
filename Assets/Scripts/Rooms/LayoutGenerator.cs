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
			Tutorial,
			Zone2,
			Zone3,
			Final,
			Credits,

			Entrance,
			TutorialPlatforms,
			TutorialMove,
			TutorialAim,
			TutorialJump,
			TutorialInteract,
			TutorialUse,
			TutorialSwap,
			TutorialThrow,
			TutorialInventory,
			TutorialSwing,
			TutorialCancel,
			TutorialCatch,
			TutorialPassThrough,
			TutorialDashDamage,
			TutorialWallJump,
			TutorialLook,
			TutorialDrop,
			TutorialDash,
			ExitDoor,
			Room,
			RoomDown,
			RoomUp,
			RoomVertical,
			RoomHorizontal,
			RoomSecret,
			RoomIndefinite,
			RoomIndefiniteCorrect,
			Npc,
			Enemy,
			Key,
			Lock, // TODO: treat as an in-between node (like {Tight/Root}Coupling)?
			LockOrdered,
			GateBreakable,
			Secret,
			BonusItems,
			Upgrade,
			Boss,
			FinalHint,
			TightCoupling,
			AreaDivider,

			Basement,
			Tower,
			Corridor,
			Sequence,
			SequenceMedium,
			SequenceLarge,
			SequenceExtraLarge,
			Gate,
			PossibleBonus
		}


		public readonly Type m_type;
		public List<Node> m_children;

		public RoomController m_room = null;


		public List<Node> DirectParents => DirectParentsInternal?.SelectMany(node => node.m_type == Type.TightCoupling || node.m_type == Type.AreaDivider ? node.DirectParents : new() { node }).ToList();

		public Node TightCoupleParent { get
		{
			if (DirectParentsInternal == null || DirectParentsInternal.Count <= 0)
			{
				return null;
			}
			if (m_type == Type.TightCoupling || m_type == Type.AreaDivider)
			{
				Debug.Assert(DirectParentsInternal.Count == 1);
				return DirectParentsInternal.First();
			}
			if (DirectParentsInternal.Count == 1)
			{
				return DirectParentsInternal.First().TightCoupleParent ?? DirectParentsInternal.First();
			}
			Node fca = FirstCommonAncestor(DirectParentsInternal);
			return fca.m_type == Type.TightCoupling || fca.m_type == Type.AreaDivider ? fca.TightCoupleParent : fca;
		} }

		public IEnumerable<Node> AreaParents => (DirectParentsInternal == null || DirectParentsInternal.Count <= 0) ? new[] { this } : (m_type == Type.AreaDivider) ? m_children : DirectParentsInternal.First().AreaParents; // TODO: don't assume all branches will converge to the same area parent?

		private int m_depthCached = -1; // TODO: mark stale when parents change?
		public int Depth { get
		{
			if (m_depthCached < 0)
			{
				List<Node> parents = DirectParents;
				m_depthCached = parents == null ? 0 : parents.Max(node => node.Depth) + (parents.Any(node => node.m_type == Type.Lock || m_type == Type.LockOrdered || m_type == Type.Secret) ? 1 : 0);
			}
			return m_depthCached;
		} }

		private int DepthMax => m_children == null || m_children.Count <= 0 ? Depth : m_children.Max(node => node.DepthMax); // TODO: efficiency?

		public float DepthPercent => (float)Depth / Math.Max(1, DepthMax); // TODO: efficiency?

		public IEnumerable<Node> WithDescendants => m_children == null ? new[] { this } : m_children.SelectMany(node => node.WithDescendants).Concat(new[] { this });

		public bool IsOptional {
			get {
				Node downstreamExit = FindDescendant(n => n.m_type == Type.ExitDoor); // TODO: handle multiple separate exits?
				if (downstreamExit == null)
				{
					return true;
				}
				return !RequiredBy(downstreamExit);
			}
		}


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

		public bool HasDescendant(Node node) => FindDescendant(n => n == node) != null;

		public Node FindDescendant(Func<Node, bool> f)
		{
			if (m_children == null)
			{
				return null;
			}
			Node matchingChild = m_children.FirstOrDefault(node => f(node));
			if (matchingChild != null)
			{
				return matchingChild;
			}
			foreach (Node child in m_children)
			{
				Node matchingDescendant = child.FindDescendant(f);
				if (matchingDescendant != null)
				{
					return matchingDescendant;
				}
			}
			return null;
		}

		public Node FirstCommonAncestor(IEnumerable<Node> nodes)
		{
			// search through ancestors breadth-first until all nodes[] entries have been accounted for
			List<Node> nodesRemaining = new(nodes); // NOTE the copy to prevent editing the original sequence
			Queue<Node> ancestorsNext = new(new[] { this });
			while (ancestorsNext.TryDequeue(out Node ancestor))
			{
				int numRemoved = nodesRemaining.RemoveAll(descendant => ancestor == descendant || ancestor.HasDescendant(descendant)); // TODO: efficiency?
				if (nodesRemaining.Count <= 0)
				{
					return ancestor;
				}

				if (numRemoved > 0)
				{
					ancestorsNext = new(); // we've found a common ancestor for some but not all of nodes[]; the overall first common ancestor must be an ancestor of this ancestor as well
				}

				foreach (Node parent in ancestor.DirectParentsInternal)
				{
					ancestorsNext.Enqueue(parent);
				}
			}

			Debug.Assert(false, "No common ancestor?");
			return null;
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
					// NOTE that we have to exclude descendantsExcluded[] from multiparentDescendants[] to avoid infinite loops due to branches w/ child branches
					IEnumerable<Node> sharedNodes = multiparentDescendants.Intersect(descendantsExcluded);
					multiparentDescendants.RemoveAll(node => sharedNodes.Contains(node));
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
			Debug.Assert(multiparentDescendants == null || multiparentDescendants.Count() <= 1); // TODO: handle multiple multi-parent descendants?
			return new(m_type, multiparentDescendants != null && multiparentDescendants.Count() > 0 ? Multiparents(childrenDuplicated, multiparentDescendants.First()) : childrenDuplicated);
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

		private bool RequiredBy(Node rhs)
		{
			if (this == rhs)
			{
				return true;
			}
			if (m_children == null || m_children.Count <= 0)
			{
				return false;
			}
			foreach (Node child in m_children)
			{
				if (child != rhs && !child.HasDescendant(rhs))
				{
					continue;
				}
				if (child.m_type == Type.Lock && m_type != Type.Key) // NOTE the assumption that all key nodes directly precede their lock node
				{
					continue;
				}
				if (child.RequiredBy(rhs))
				{
					return true;
				}
			}
			return false;
		}
	}


	private struct ReplacementRule
	{
		public Node.Type m_initial; // TODO: convert to Node, to allow input chains?
		public List<Node> m_final;
		public float m_weight;

		public ReplacementRule(Node.Type initial, List<Node> final, float weight = 1.0f) { m_initial = initial; m_final = final; m_weight = weight; }
	}

	// TODO: move into data?
	private static readonly ReplacementRule[] m_rules =
	{
		new(Node.Type.Tutorial, new() { new(Node.Type.Entrance, new() { new(Node.Type.TutorialPlatforms), new(Node.Type.TutorialAim), new(Node.Type.TutorialMove, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomDown, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.TutorialJump, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomUp, new() { new(Node.Type.TutorialInteract), new(Node.Type.TutorialUse), new(Node.Type.TutorialSwap), new(Node.Type.TutorialThrow), new(Node.Type.GateBreakable, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.ExitDoor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.TutorialInventory), new(Node.Type.TutorialSwing), new(Node.Type.TutorialCancel), new(Node.Type.TutorialCatch), new(Node.Type.TutorialPassThrough, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.TutorialDashDamage), new(Node.Type.TutorialWallJump), new(Node.Type.TutorialLook), new(Node.Type.TutorialDrop), new(Node.Type.TutorialDash) }) }) }) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Entryway, new() { new(Node.Type.Entrance, Node.Multiparents(new() { new(Node.Type.AreaDivider, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.ExitDoor, new() { new(Node.Type.LockOrdered, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.ExitDoor, new() { new(Node.Type.LockOrdered, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomUp, new() { new(Node.Type.ExitDoor) }) }) }) }) }) }) }) }) }) }), new(Node.Type.Basement, new() { new(Node.Type.Secret, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.RoomIndefiniteCorrect, new() { new(Node.Type.Secret) }) }) }) }), new(Node.Type.Tower), new(Node.Type.Corridor) }, new(Node.Type.Npc, new() { new(Node.Type.Npc), new(Node.Type.Npc), new(Node.Type.Npc) }))) }), // TODO: move directional requirement into data?
		new(Node.Type.Zone1, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceMedium, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceLarge, new() { new(Node.Type.Secret, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.RoomSecret), new(Node.Type.FinalHint) }) }), new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.Boss, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Npc), new(Node.Type.ExitDoor), new(Node.Type.ExitDoor), new(Node.Type.FinalHint) }) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Zone2, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceLarge, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceExtraLarge, new() { new(Node.Type.Secret, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.RoomSecret), new(Node.Type.FinalHint) }) }), new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.Boss, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Npc), new(Node.Type.ExitDoor), new(Node.Type.ExitDoor), new(Node.Type.FinalHint) }) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Zone3, new() { new(Node.Type.Entrance, new() { new(Node.Type.Key, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceMedium, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceLarge, new() { new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.SequenceExtraLarge, new() { new(Node.Type.Secret, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.RoomSecret), new(Node.Type.FinalHint) }) }), new(Node.Type.Lock, new() { new(Node.Type.AreaDivider, new() { new(Node.Type.Boss, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Npc), new(Node.Type.ExitDoor), new(Node.Type.FinalHint) }) }) }) }) }) }) }) }) }) }) }) }) }) }) }) }),
		new(Node.Type.Final, new() { new(Node.Type.Entrance, new() { new(Node.Type.Corridor, new() { new(Node.Type.Tower, new() { new(Node.Type.Tower, new() { new(Node.Type.Boss) }) }) }) }) }),
		new(Node.Type.Credits, new() { new(Node.Type.Entrance, new() { new(Node.Type.Entrance + 1, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Entrance + 2, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Entrance + 3) }) }) }) }) }) }), // TODO: more generic Text{1-n} Types?

		new(Node.Type.Basement, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomDown, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical) }) }) }) }) }) }),
		new(Node.Type.Tower, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomUp, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomVertical) }) }) }) }) }) }) }) }),
		new(Node.Type.Corridor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal) }) }) }) }) }) }),
		new(Node.Type.Corridor, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.RoomHorizontal) }) }) }) }) }) }) }) }),

		new(Node.Type.SequenceMedium, new() { new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceLarge, new() { new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),
		new(Node.Type.SequenceExtraLarge, new() { new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence), new(Node.Type.Sequence) }),

		// serial chains
		// NOTE that the leaf Keys are required for the following Locks
		new(Node.Type.Sequence, new() { new(Node.Type.Gate, new() { new(Node.Type.Key), new(Node.Type.PossibleBonus) }) }),
		new(Node.Type.Sequence, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Key) }) }) }),
		new(Node.Type.Sequence, new() { new(Node.Type.Room, new() { new(Node.Type.TightCoupling, new() { new(Node.Type.Enemy), new(Node.Type.Key) }) }) }),
		new(Node.Type.PossibleBonus, null, 2.0f),
		new(Node.Type.PossibleBonus, new() { new(Node.Type.Gate, new() { new(Node.Type.BonusItems) }) }),
		new(Node.Type.PossibleBonus, new() { new(Node.Type.Gate, new() { new(Node.Type.Upgrade) }) }),

		// gate types
		new(Node.Type.Gate, new() { new(Node.Type.GateBreakable, new() { new(Node.Type.TightCoupling) }) }, 0.5f),
		new(Node.Type.Gate, new() { new(Node.Type.Key, new() { new(Node.Type.Lock, new() { new(Node.Type.TightCoupling) }) }) }),
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

			bool done = next.m_type != Node.Type.TightCoupling && next.m_type != Node.Type.AreaDivider && f(next); // NOTE the deliberate skip of internal-only nodes
			if (done)
			{
				return true;
			}
		}

		return false;
	}
}
