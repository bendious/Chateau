using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering.Universal;
using UnityEngine.U2D;
using UnityEngine.VFX;


[DisallowMultipleComponent]
public class RoomController : MonoBehaviour
{
	[System.Serializable] private struct DirectionalDoors
	{
		public Vector2 m_direction;
		public WeightedObject<GameObject>[] m_prefabs;
		public bool m_suppressLadders;
	};


	[SerializeField] private WeightedObject<GameObject>[] m_gatePrefabs;
	[SerializeField] private DirectionalDoors[] m_doorDirectionalPrefabs;
	[SerializeField] private WeightedObject<GameObject>[] m_doorSecretPrefabs;
	[SerializeField] private WeightedObject<GameObject>[] m_cutbackPrefabs;

	[SerializeField] private WeightedObject<GameObject>[] m_exteriorPrefabsAbove;
	[SerializeField] private WeightedObject<GameObject>[] m_exteriorPrefabsWithin;
	[SerializeField] private WeightedObject<GameObject>[] m_exteriorPrefabsOnGround;
	[SerializeField] private WeightedObject<GameObject>[] m_exteriorPrefabsBelow;

	[SerializeField] private Sprite m_floorPlatformSprite;
	[SerializeField] private WeightedObject<VisualEffect>[] m_doorSealVFX;

	[SerializeField] private WeightedObject<GameObject>[] m_spawnPointPrefabs;
	[SerializeField] private float m_spawnPointHeightMax = 6.0f;

	public GameObject m_backdrop;
	public PolygonCollider2D m_cameraConstraint;

	[SerializeField] private GameObject[] m_walls;

	[System.Serializable] public class LadderInfo
	{
		public GameObject m_rungPrefab;
		public bool m_singleRung;
	}
	[SerializeField] private WeightedObject<LadderInfo>[] m_ladderRungPrefabs; // NOTE that this is combined w/ GameController.m_ladderRungPrefabs[] before selection
	[SerializeField] private float m_ladderRungSkewMax = 0.2f;

	[SerializeField] private float m_cutbackBreakablePct = 0.5f;
	[SerializeField] private float m_noLadderNoPlatformPct = 0.5f;
	[SerializeField] private float m_furnitureLockPct = 0.5f;

	public static readonly Color m_oneWayPlatformColor = new(0.3f, 0.2f, 0.1f);


	public enum PathFlags
	{
		None = 0,
		ObstructionCheck = 1,
		Directional = 3, // NOTE that Directional includes ObstructionCheck since it doesn't make sense separately
		IgnoreGravity = 4,
		NearestEndPoints = 8,
	};


	public IEnumerable<System.Tuple<GameObject, RoomController>> DoorwaysUpwardOpen => m_doorwayInfos.Where(info => info.IsOpen(true) && info.DirectionOutward() == Vector2.up).Select(info => System.Tuple.Create(info.m_object, info.ConnectedRoom));

	public /*readonly*/ Bounds Bounds { get; private set; }

	public Bounds BoundsInterior { get {
		Bounds boundsInterior = Bounds; // NOTE the copy since Expand() modifies the given struct
		boundsInterior.Expand(new Vector3(-1.0f, -1.0f, float.MaxValue)); // TODO: dynamically determine wall thickness?
		return boundsInterior;
	} }

	public RoomController Parent => m_doorwayInfos.Select(info => info.ParentRoom).FirstOrDefault(room => room != null);
	public RoomController FirstChild => m_doorwayInfos.Select(info => info.ChildRoom).FirstOrDefault(room => room != null);

	public Vector2 ParentDoorwayPosition => Connection(Parent, PathFlags.None, Vector2.zero)[1];

	public IEnumerable<RoomController> WithDescendants => new[] { this }.Concat(m_doorwayInfos.Where(info => info.ChildRoom != null).SelectMany(info => info.ChildRoom.WithDescendants)); // TODO: efficiency?

	public RoomType RoomType { get; private set; }


	[System.Serializable]
	private class DoorwayInfo
	{
		public /*readonly*/ GameObject m_object;

		public bool m_disallowLadders = false;


		internal RoomController ParentRoom
		{
			get => m_connectionType != ConnectionType.Parent ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.Parent; ConnectedRoom = value; }
		}
		internal RoomController ChildRoom
		{
			get => m_connectionType != ConnectionType.Child ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.Child; ConnectedRoom = value; }
		}
		internal RoomController SiblingShallowerRoom
		{
			get => m_connectionType != ConnectionType.SiblingShallower ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.SiblingShallower; ConnectedRoom = value; }
		}
		internal RoomController SiblingDeeperRoom
		{
			get => m_connectionType != ConnectionType.SiblingDeeper ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.SiblingDeeper; ConnectedRoom = value; }
		}
		internal RoomController ConnectedRoom { get; private set; }

		internal GameObject m_blocker;

		internal enum BlockageType { None, Destructible, NoLadder };
		internal BlockageType m_onewayBlockageType;

		internal /*readonly*/ DoorwayInfo m_infoReverse;

		internal System.Lazy<bool> m_excludeSelf = new(() => Random.value < 0.5f, false); // TODO: more deliberate determination?


		internal RoomController Room => m_object.transform.parent.GetComponent<RoomController>(); // TODO: cache reference?


		private enum ConnectionType { None, Parent, Child, SiblingShallower, SiblingDeeper };
		private ConnectionType m_connectionType;


		internal void FlipSiblingDirection()
		{
			Debug.Assert((m_connectionType == ConnectionType.SiblingShallower && m_infoReverse.m_connectionType == ConnectionType.SiblingDeeper) || (m_connectionType == ConnectionType.SiblingDeeper && m_infoReverse.m_connectionType == ConnectionType.SiblingShallower));
			m_connectionType = m_connectionType == ConnectionType.SiblingShallower ? ConnectionType.SiblingDeeper : ConnectionType.SiblingShallower;
			m_infoReverse.m_connectionType = m_infoReverse.m_connectionType == ConnectionType.SiblingShallower ? ConnectionType.SiblingDeeper : ConnectionType.SiblingShallower;
		}

		internal Vector2 Size() => m_object.GetComponent<BoxCollider2D>().size * m_object.transform.localScale; // NOTE that we can't use Collider2D.bounds since this can be called before physics has run

		internal Vector2 DirectionOutward()
		{
			// TODO: don't assume convex room shapes?
			Vector2 roomToDoorway = (Vector2)m_object.transform.position - (Vector2)m_object.transform.parent.transform.position;
			Vector3 doorwaySize = Size();
			return doorwaySize.x > doorwaySize.y ? new(0.0f, Mathf.Sign(roomToDoorway.y)) : new(Mathf.Sign(roomToDoorway.x), 0.0f);
		}

		internal bool IsObstructed(PathFlags flags, bool ignoreOnewayBlockages = false)
		{
			RoomController room = Room;
			Debug.Assert(room != null && room.m_doorwayInfos.Contains(this));
			if (!flags.BitsSet(PathFlags.ObstructionCheck))
			{
				return false;
			}
			if (flags.BitsSet(PathFlags.Directional) && ((m_connectionType == ConnectionType.SiblingShallower && m_onewayBlockageType != BlockageType.NoLadder) || room.LayoutNodes.Any(fromNode => fromNode.m_children != null && fromNode.m_children.Count > 0 && fromNode.m_children.All(toNode => ConnectedRoom.LayoutNodes.Contains(toNode))))) // TODO: take areas into account and the fact that earlier area exits are guaranteed to be traversable once later areas are accessed?
			{
				return false;
			}
			if (!ignoreOnewayBlockages && (flags.BitsSet(PathFlags.IgnoreGravity) ? m_onewayBlockageType == BlockageType.Destructible : m_onewayBlockageType != BlockageType.None)) // TODO: check reverse one-way sometimes?
			{
				return true;
			}
			if (!IsOpen(true) || !m_infoReverse.IsOpen(true)) // NOTE that we check m_blocker separately from IsOpen(), since destructible blockers sometimes need to be ignored
			{
				return true;
			}
			if (m_blocker != null && m_blocker.GetComponents<Collider2D>().Any(collider => !collider.isTrigger && collider.isActiveAndEnabled) && (!flags.BitsSet(PathFlags.Directional) || m_blocker.GetComponent<IUnlockable>() != null)) // NOTE that m_infoReverse.m_blocker should always be the same as m_blocker, so we only check one; also, we don't need to worry about one-way destructibles here since we've already checked them in the PathFlags.Directional branch above
			{
				return true;
			}
			return false;
		}

		internal bool IsOpen(bool ignoreBlocker = false)
		{
			if (!ignoreBlocker && m_blocker != null && m_blocker.GetComponents<Collider2D>().Any(collider => !collider.isTrigger && collider.isActiveAndEnabled))
			{
				return false;
			}

			if (!m_object.activeSelf)
			{
				return true;
			}

			PlatformEffector2D platform = m_object.GetComponent<PlatformEffector2D>();
			if (platform != null && platform.enabled)
			{
				return true;
			}

			return false;
		}

		internal float AdjacentRoomPct()
		{
			if (ConnectedRoom != null)
			{
				// rely on adjacent room's doorways
				return ConnectedRoom.m_doorwayInfos.Count(i => i.ConnectedRoom != null) / (float)ConnectedRoom.m_doorwayInfos.Length;
			}

			// query GameController about surrounding space
			int roomsFound = 0;
			const int roomsMax = 4; // TODO: determine dynamically?
			/*const*/ Vector2 gridSize = new(15.0f, 9.0f); // TODO: determine dynamically based on room prefabs?
			Vector2 centerPos = (Vector2)m_object.transform.position + DirectionOutward() * gridSize * 0.5f;
			for (int i = 0; i < roomsMax; ++i)
			{
				Vector2 checkPos = centerPos + Quaternion.Euler(0.0f, 0.0f, 90.0f * i) * Vector2.right * gridSize;
				if (GameController.Instance.RoomFromPosition(checkPos) != null)
				{
					++roomsFound;
				}
			}
			Debug.Assert(roomsFound > 0, "DoorwayInfo.AdjacentRoomPct() unable to find even the doorway's own room?");
			return roomsFound / (float)roomsMax;
		}
	}
	[SerializeField]
	private /*readonly*/ DoorwayInfo[] m_doorwayInfos;

	public LayoutGenerator.Node[] LayoutNodes { get; private set; }

	private /*readonly*/ GameObject[] m_spawnPoints;

	private /*readonly*/ RoomType.SpriteInfo m_wallInfo;
	private /*readonly*/ Color m_wallColor;


	private const float m_physicsCheckEpsilon = 0.1f; // NOTE that Utility.FloatEpsilon is too small to prevent false positives from rooms adjacent to the checked area


	private static readonly List<RoomController> m_runtimeRooms = new();


	public static T RandomWeightedByKeyCount<T>(IEnumerable<WeightedObject<T>> candidates, System.Func<T, int, LockController.KeyStats> candidateToStats, int preferredKeyCount, float difficultyPct, float scalarPerDiff = 0.5f)
	{
		float difficultyDesired = Mathf.Max(0.0f, Mathf.LerpUnclamped(GameController.Instance.m_difficultyMin, GameController.Instance.m_difficultyMax, difficultyPct));

		// NOTE the copy to avoid altering existing weights
		WeightedObject<T>[] candidatesProcessed = candidates.Where(candidate => candidate.m_object != null).Select(candidate =>
		{
			LockController.KeyStats keyDifficultyRanges = candidateToStats(candidate.m_object, preferredKeyCount);
			Debug.Assert(keyDifficultyRanges.IsValid);
			int keyDiff = keyDifficultyRanges.m_keyRoomsMinMax.x <= preferredKeyCount && preferredKeyCount <= keyDifficultyRanges.m_keyRoomsMinMax.y ? 0 : preferredKeyCount < keyDifficultyRanges.m_keyRoomsMinMax.x ? keyDifficultyRanges.m_keyRoomsMinMax.x - preferredKeyCount : keyDifficultyRanges.m_keyRoomsMinMax.y - preferredKeyCount;
			float difficultyDiff = keyDifficultyRanges.m_difficultyMinMax.x <= difficultyDesired && difficultyDesired <= keyDifficultyRanges.m_difficultyMinMax.y ? 0.0f : difficultyDesired < keyDifficultyRanges.m_difficultyMinMax.x ? keyDifficultyRanges.m_difficultyMinMax.x - difficultyDesired : keyDifficultyRanges.m_difficultyMinMax.y - difficultyDesired;
			float weight = keyDiff < 0 || keyDifficultyRanges.m_difficultyMinMax.x > GameController.Instance.m_difficultyMax ? 0.0f : candidate.m_weight / (1 + (keyDiff + Mathf.Abs(difficultyDiff)) * scalarPerDiff); // NOTE that we hard-prevent too-difficult puzzles but only discourage too-easy puzzles
			return new WeightedObject<T> { m_object = candidate.m_object, m_weight = weight };
		}).ToArray();
		return candidatesProcessed.Length <= 0 ? default : candidatesProcessed.All(pair => pair.m_weight <= 0.0f) ? candidatesProcessed.Random().m_object : candidatesProcessed.RandomWeighted(); // NOTE that we handle all candidates being "excluded", since that really just means "suboptimal"
	}

	public static LockController.KeyStats ObjectToKeyStats(GameObject prefab, int preferredKeyCount)
	{
		LockController[] locks = prefab.GetComponents<LockController>();
		if (locks.Length <= 0)
		{
			if (prefab.TryGetComponent(out GateController gate))
			{
				locks = gate.m_lockPrefabs.Select(info => info.m_object.m_prefab.GetComponent<LockController>()).ToArray();
			}
		}
		return locks.Length <= 0 ? new LockController.KeyStats() : locks.Aggregate(LockController.KeyStats.Invalid, (sum, nextLock) => sum.Aggregate(nextLock.ToKeyStats(preferredKeyCount)));
	}


	private void Awake()
	{
		Bounds = m_backdrop.GetComponent<SpriteRenderer>().bounds;
		ObjectDespawn.OnExecute += OnObjectDespawn;
	}

	private void OnDestroy()
	{
		ObjectDespawn.OnExecute -= OnObjectDespawn;
		GameController.Instance.RemoveRootRoom(this);
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (LayoutNodes == null || ConsoleCommands.LayoutDebugLevel == (int)ConsoleCommands.LayoutDebugLevels.None)
		{
			return;
		}

		Vector3 centerPosItr = Bounds.center;
		if (RoomType != null)
		{
			UnityEditor.Handles.Label(centerPosItr, RoomType.ToString()); // TODO: prevent drift from Scene camera?
		}

		foreach (LayoutGenerator.Node node in LayoutNodes)
		{
			centerPosItr.y -= 1.0f;
			UnityEditor.Handles.Label(centerPosItr, node.m_type.ToString() + " (depth " + node.Depth + ")"); // TODO: prevent drift from Scene camera?

			List<LayoutGenerator.Node> parentsCached = node.DirectParents;
			if (parentsCached == null)
			{
				continue;
			}

			if (ConsoleCommands.LayoutDebugLevel == ConsoleCommands.LayoutDebugLevels.DirectParents || ConsoleCommands.LayoutDebugLevel == ConsoleCommands.LayoutDebugLevels.All)
			{
				foreach (LayoutGenerator.Node parentNode in parentsCached)
				{
					UnityEditor.Handles.DrawLine(centerPosItr, parentNode.m_room.transform.position);
				}
			}

			if (ConsoleCommands.LayoutDebugLevel == ConsoleCommands.LayoutDebugLevels.TightParents || ConsoleCommands.LayoutDebugLevel == ConsoleCommands.LayoutDebugLevels.All)
			{
				LayoutGenerator.Node tightParent = node.TightCoupleParent;
				if (tightParent != null)
				{
					using (new UnityEditor.Handles.DrawingScope(Color.red))
					{
						UnityEditor.Handles.DrawLine(centerPosItr, tightParent.m_room.transform.position);
					}
				}
			}
		}

		foreach (DoorwayInfo info in m_doorwayInfos)
		{
			if (info.m_onewayBlockageType != DoorwayInfo.BlockageType.None)
			{
				// draw simple arrow pointing inward to indicate allowed direction
				using (new UnityEditor.Handles.DrawingScope(info.m_onewayBlockageType == DoorwayInfo.BlockageType.Destructible ? Color.gray : Color.white))
				{
					UnityEditor.Handles.DrawLines(new[] { info.m_object.transform.position + new Vector3(0.5f, 0.5f), info.m_object.transform.position, info.m_object.transform.position, 2.0f * info.m_infoReverse.m_object.transform.position - info.m_object.transform.position });
				}
			}
		}
	}
#endif


	public void SetNodes(LayoutGenerator.Node[] layoutNodes)
	{
		LayoutNodes = layoutNodes;
		foreach (LayoutGenerator.Node node in LayoutNodes)
		{
			Debug.Assert(node.m_room == null);
			node.m_room = this;
		}
	}

	public void FinalizeRecursive(ref int npcDepth)
	{
		// record/increment depths since we need the original values after passing the incremented values to our descendants
		int npcDepthLocal = npcDepth;
		npcDepth += LayoutNodes.Count(node => node.m_type == LayoutGenerator.Node.Type.Npc);

		// spawn fixed-placement node architecture
		// NOTE the separate loops to ensure fixed-placement nodes are processed before flexible ones; also that this needs to be before flexibly-placed objects such as furniture
		float fillPct = 0.0f;
		Vector2 extentsInterior = BoundsInterior.extents;
		foreach (LayoutGenerator.Node node in LayoutNodes)
		{
			switch (node.m_type)
			{
				case LayoutGenerator.Node.Type.Entrance:
					fillPct += SpawnDoor(0, true, extentsInterior.x);
					break;
			}
		}

		// open cutbacks
		// NOTE that this has to be before flexible-placement spawning to avoid overlap w/ ladders
		if (GameController.Instance.m_allowCutbacks && LayoutNodes.All(node => node.m_type != LayoutGenerator.Node.Type.RoomSecret && node.m_type != LayoutGenerator.Node.Type.RoomIndefinite && node.m_type != LayoutGenerator.Node.Type.RoomIndefiniteCorrect))
		{
			foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
			{
				if (doorwayInfo.ConnectedRoom != null)
				{
					continue;
				}

				// check for potential sibling
				Vector2 direction = doorwayInfo.DirectionOutward();
				GameObject doorway = doorwayInfo.m_object;
				Vector2 doorwayPos = doorway.transform.position;
				RoomController sibling = FromPosition(doorwayPos + direction);
				if (sibling == null || sibling.LayoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomSecret || node.m_type == LayoutGenerator.Node.Type.RoomIndefinite || node.m_type == LayoutGenerator.Node.Type.RoomIndefiniteCorrect)) // TODO: allow some cutbacks in indefinite room generation?
				{
					continue;
				}

				// fetch sibling doorway info
				int reverseIdx = -1;
				foreach (int siblingInfoIdx in sibling.DoorwayReverseIndices(direction))
				{
					if (Vector2.Distance(sibling.m_doorwayInfos[siblingInfoIdx].m_object.transform.position, doorwayPos) < 1.0f/*?*/)
					{
						reverseIdx = siblingInfoIdx;
						break;
					}
				}
				if (reverseIdx < 0)
				{
					continue;
				}
				DoorwayInfo reverseInfo = sibling.m_doorwayInfos[reverseIdx];

				// determine relationships
				int siblingDepthComparison = sibling.LayoutNodes.Max(node => node.Depth).CompareTo(LayoutNodes.Max(node => node.Depth));
				RoomController deepRoom = siblingDepthComparison < 0 ? this : sibling;
				RoomController shallowRoom = deepRoom == this ? sibling : this;
				RoomController lowRoom = direction.y.FloatEqual(0.0f) ? deepRoom : (direction.y > 0.0f ? this : sibling);
				bool noLadder = deepRoom != lowRoom;

				if (!noLadder && (lowRoom == this ? doorwayInfo : reverseInfo).m_disallowLadders)
				{
					continue; // TODO: detect & allow if one-way loop creation would set noLadder below?
				}

				// determine traversability before adding cutback
				// TODO: use avatar max jump height once RoomPath() takes platforms into account?
				const PathFlags flags = PathFlags.ObstructionCheck | PathFlags.Directional;
				AStarPath pathShallowToDeep = shallowRoom.RoomPath(shallowRoom.gameObject, deepRoom.gameObject, flags);
				AStarPath pathDeepToShallow = deepRoom.RoomPath(deepRoom.gameObject, shallowRoom.gameObject, flags);
				bool cutbackIsLocked = doorwayInfo.m_onewayBlockageType == DoorwayInfo.BlockageType.Destructible || (shallowRoom == lowRoom && siblingDepthComparison != 0 ? !noLadder : pathShallowToDeep == null);

				// connect doorways
				if (deepRoom == this)
				{
					doorwayInfo.SiblingShallowerRoom = sibling;
					reverseInfo.SiblingDeeperRoom = this;
				}
				else
				{
					doorwayInfo.SiblingDeeperRoom = sibling;
					reverseInfo.SiblingShallowerRoom = this;
				}
				doorwayInfo.m_infoReverse = reverseInfo;
				reverseInfo.m_infoReverse = doorwayInfo;

				// check for one-way loop traversal
				bool canTraverseForward = pathShallowToDeep != null && (!noLadder || deepRoom != lowRoom);
				bool canTraverseBackward = pathDeepToShallow != null && !cutbackIsLocked && (!noLadder || shallowRoom != lowRoom);
				if (canTraverseForward || canTraverseBackward)
				{
					int pathIdx = 0;
					int posIdx = -1;
					AStarPath path = !canTraverseForward || (canTraverseBackward && Random.value < 0.5f) ? pathDeepToShallow : pathShallowToDeep; // TODO: make both paths one-way if they don't intersect?
					DoorwayInfo[] pathDoorways = path.m_pathRooms.GetRange(0, path.m_pathRooms.Count - 1).Select(room =>
					{
						++pathIdx;
						posIdx += 2; // TODO: don't assume two points per room?
						return room.m_doorwayInfos.First(info => info.ConnectedRoom == path.m_pathRooms[pathIdx] && (path.m_pathPositions[posIdx] - (Vector2)info.m_object.transform.position).sqrMagnitude < 1.0f/*?*/); // TODO: simpler way of ensuring the correct doorway for room pairs w/ multiple connections?
					}).ToArray();

					// try creating one-ways
					foreach (DoorwayInfo infoForward in pathDoorways)
					{
						DoorwayInfo infoReverse = infoForward.m_infoReverse; // NOTE that the blocks are in the direction in which the path is NOT going
						if (infoReverse.DirectionOutward() == Vector2.up)
						{
							infoReverse.m_onewayBlockageType = DoorwayInfo.BlockageType.NoLadder; // NOTE that it's okay if we overwrite an existing value of Destructible; NoLadder should take priority since there should never be a situation where a ladder is dynamically spawned in a doorway that also has a one-way destructible
						}
						else if (infoReverse.m_blocker == null)
						{
							// block w/ a destructible if the siblings are or can be oriented in the desired direction
							// TODO: allow orienting destructible one-ways in either deep-->shallow or shallow-->deep orientations?
							if (infoReverse.SiblingShallowerRoom != null && infoReverse.SiblingShallowerRoom.LayoutNodes.Max(node => node.Depth) == infoReverse.Room.LayoutNodes.Max(node => node.Depth))
							{
								infoReverse.FlipSiblingDirection();
							}
							if (infoReverse.SiblingDeeperRoom != null)
							{
								infoReverse.m_onewayBlockageType = DoorwayInfo.BlockageType.Destructible;
							}
						}
					}

					// TODO: restrict the main cutback doorway when possible?
				}

				// maybe add one-way lock
				Debug.Assert(noLadder || cutbackIsLocked || GameController.Instance.m_isHubScene || LayoutNodes.First().AreaParents.Zip(sibling.LayoutNodes.First().AreaParents, System.Tuple.Create).All(pair => pair.Item1 == pair.Item2), "Open cutback between separate areas?"); // TODO: don't assume hub scenes are open-concept?
				if (cutbackIsLocked || Random.value <= m_cutbackBreakablePct)
				{
					// add one-way lock
					int dummyLockIdx = -1;
					noLadder |= shallowRoom.SpawnGate(shallowRoom != this ? doorwayInfo : reverseInfo, cutbackIsLocked ? LayoutGenerator.Node.Type.Lock : LayoutGenerator.Node.Type.GateBreakable, !cutbackIsLocked || doorwayInfo.m_excludeSelf.Value ? 0 : 1, cutbackIsLocked ? 0.0f : float.MinValue, ref dummyLockIdx, true, false); // NOTE the "reversed" DoorwayInfos to place the gate in deepRoom but as a child of shallowRoom, for better shadowing
				}

				if (noLadder)
				{
					(direction.y > 0.0f ? doorwayInfo : reverseInfo).m_onewayBlockageType = DoorwayInfo.BlockageType.NoLadder; // NOTE that it's okay if we overwrite an existing value of Destructible; NoLadder should take priority since there should never be a situation where a ladder is dynamically spawned in a doorway that also has a one-way destructible
					if (doorwayInfo.m_blocker == null && Random.value < m_noLadderNoPlatformPct)
					{
						// non-platform hole
						// TODO: AI pathfinding jump marker(s); prevent floating furniture? ensure falling through never results in softlock
						GameObject platformDoorway = direction.y > 0.0f ? reverseInfo.m_object : doorwayInfo.m_object;
						DestroyImmediate(platformDoorway.GetComponent<PlatformEffector2D>()); // NOTE that we can't just disable the effector since SealRoom() would end up turning it back on, and we have to do destruction immediately since the wall color logic below checks it this frame...
						platformDoorway.GetComponent<Collider2D>().usedByEffector = false;
						platformDoorway.layer = GameController.Instance.m_layerWalls.ToIndex();
						platformDoorway.SetActive(false);
					}
				}

				// open doorways
				// NOTE that this has to be AFTER checking RoomPath() above
				OpenDoorway(doorwayInfo, true);
				sibling.OpenDoorway(reverseInfo, true);
			}
		}

		// room type
		// TODO: more deliberate choice?
		List<float> weightsScaled = new();
		RoomType = (LayoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.Boss) ? GameController.Instance.m_roomTypesBoss : LayoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomSecret) ? GameController.Instance.m_roomTypesSecret : GameController.Instance.m_roomTypes).Where(type =>
		{
			float weightScaled = type.m_weight;
			if (type.m_object.m_preconditionNames == null)
			{
				weightsScaled.Add(weightScaled);
				return true;
			}
			foreach (string preconditionName in type.m_object.m_preconditionNames)
			{
				SendMessageValue<float> result = new() { m_out = 1.0f };
				SendMessage(preconditionName, result);
				if (result.m_out <= 0.0f)
				{
					return false;
				}
				weightScaled *= result.m_out;
			}
			weightsScaled.Add(weightScaled);
			return true;
		}).Select(weightedObj => weightedObj.m_object).ToArray().RandomWeighted(weightsScaled); // NOTE the ToArray() call to avoid an order-of-operations issue in RandomWeighted() since weightsScaled[] isn't filled out until the values are evaluated

		// backdrop
		if (RoomType.m_backdrops != null && RoomType.m_backdrops.Length > 0)
		{
			RoomType.SpriteInfo backdrop = RoomType.m_backdrops.RandomWeighted();
			SpriteRenderer renderer = m_backdrop.GetComponent<SpriteRenderer>();
			renderer.sprite = backdrop.m_sprite;
			renderer.color = Utility.ColorRandom(backdrop.m_colorMin, backdrop.m_colorMax, backdrop.m_proportionalColor);
		}

		// per-area appearance
		// TODO: separate RoomType into {Area/Room}Type? tend brighter based on progress?
		IEnumerable<RoomController> areaParents = LayoutNodes.SelectMany(node => node.AreaParents).Select(node => node.m_room).Distinct();
		RoomController areaParent = areaParents.FirstOrDefault(room => room.m_wallInfo != null && RoomType.m_walls.Any(wall => wall.m_object.m_sprite == room.m_wallInfo.m_sprite));
		bool isAreaInit = areaParent == null;
		if (isAreaInit)
		{
			areaParent = areaParents.All(parent => parent.m_wallInfo == null) ? areaParents.First() : this;
		}
		m_wallInfo = areaParent.m_wallInfo ?? (RoomType.m_walls != null && RoomType.m_walls.Length > 0 ? RoomType.m_walls.RandomWeighted() : new());
		m_wallColor = isAreaInit ? Utility.ColorRandom(m_wallInfo.m_colorMin, m_wallInfo.m_colorMax, m_wallInfo.m_proportionalColor) : areaParent.m_wallColor;
		if (m_wallInfo.m_sprite != null)
		{
			foreach (GameObject obj in m_walls.Concat(m_doorwayInfos.Select(info => info.m_object)))
			{
				PlatformEffector2D platform = obj.GetComponent<PlatformEffector2D>();
				if (platform != null && platform.enabled)
				{
					continue; // ignore one-way platforms
				}
				SpriteRenderer renderer = obj.GetComponent<SpriteRenderer>();
				renderer.sprite = m_wallInfo.m_sprite;
				renderer.color = m_wallColor; // TODO: slight variation?
			}
			foreach (DoorwayInfo info in m_doorwayInfos)
			{
				GameObject obj = info.m_blocker;
				if (obj == null || obj.transform.parent != transform || obj.GetComponent<IUnlockable>() != null || obj.GetComponent<ColorRandomizer>() != null)
				{
					continue;
				}
				// NOTE that we leave the sprite alone since this is for secret gates
				obj.GetComponent<SpriteRenderer>().color = m_wallColor; // TODO: slight variation?
			}
		}

		// randomize non-square walls
		if (RoomType.m_nonsquareShape != null)
		{
			foreach (GameObject wall in m_walls.Concat(m_doorwayInfos.Select(info => info.m_object)))
			{
				if (!wall.TryGetComponent(out PolygonCollider2D collider))
				{
					continue;
				}

				// switch from box to polygon collider
				BoxCollider2D colliderPrev = wall.GetComponent<BoxCollider2D>();
				colliderPrev.enabled = false;
				collider.enabled = true;

				// switch from SpriteRenderer to SpriteShape
				// TODO: assign parameters in Inspector rather than here? avoid creating components on-the-fly?
				DestroyImmediate(wall.GetComponent<SpriteRenderer>()); // can't defer destruction since SpriteShapeRenderer conflicts w/ SpriteRenderer // TODO: efficiency?
				SpriteShapeController shapeController = wall.AddComponent<SpriteShapeController>();
				shapeController.spriteShape = RoomType.m_nonsquareShape;
				shapeController.autoUpdateCollider = true;
				shapeController.colliderDetail = 100; // TODO: parameterize/vary?
				shapeController.splineDetail = shapeController.colliderDetail;
				shapeController.spriteShapeRenderer.color = m_wallColor;
				if (RoomType.m_nonsquareMaterialFill != null || RoomType.m_nonsquareMaterialEdge != null)
				{
					Material[] materialsPrev = shapeController.spriteShapeRenderer.materials;
					shapeController.spriteShapeRenderer.materials = new[] { RoomType.m_nonsquareMaterialFill != null ? RoomType.m_nonsquareMaterialFill : materialsPrev[0], RoomType.m_nonsquareMaterialEdge != null ? RoomType.m_nonsquareMaterialEdge : materialsPrev[1] };
				}

				// get shape info
				Vector2 extents = colliderPrev.size * 0.5f; // NOTE that we can't use Bounds.extents since this is before it has existed for a physics frame
				bool isVertical = extents.y > extents.x;
				bool isNegative = isVertical ? wall.transform.localPosition.x > 0.0f : wall.transform.localPosition.y > 0.0f;
				float extentMax = Mathf.Max(extents.x, extents.y);
				Spline spline = shapeController.spline;
				List<Sprite> sprites = RoomType.m_nonsquareShape.angleRanges.First().sprites; // TODO: support multiple angle ranges?
				Sprite sprite = sprites.FirstOrDefault(s => s != null); // TODO: don't assume all sprites have equivalent size?
				float spriteWidth = sprite != null ? sprite.texture.width / sprite.pixelsPerUnit : 1.0f; // TODO: parameterize default value?
				int numPoints = Mathf.RoundToInt(extentMax * 2.0f / spriteWidth) + 2;
				const float heightMin = 0.0f;
				float heightMax = spriteWidth; // TODO: parameterize?

				// randomize SpriteShapeController.spline
				float xStart = isVertical == isNegative ? -extentMax : extentMax;
				float xEnd = isVertical == isNegative ? extentMax : -extentMax;
				Vector3[] points = HeightSplinePoints(numPoints, isNegative, isVertical, true, xStart, xEnd, extentMax, Mathf.Min(extents.x, extents.y), heightMin);
				for (int i = 0; i < numPoints; ++i)
				{
					bool isEndpoint = i >= numPoints - 2;
					spline.InsertPointAt(i, points[i]);
					spline.SetTangentMode(i, isEndpoint ? ShapeTangentMode.Linear : ShapeTangentMode.Continuous); // TODO: parameterize / move into tangentFunc()?
					spline.SetHeight(i, Random.value); // TODO: more deliberate choice / continuity?
					if (!isEndpoint && i < numPoints - 3)
					{
						spline.SetSpriteIndex(i, Random.Range(0, sprites.Count));
					}
				}
				for (int i = 0; i < numPoints; ++i)
				{
					System.Tuple<Vector3, Vector3> tangents = TangentsFromSpline(spline, wall.transform.position, i, numPoints, false);
					spline.SetLeftTangent(i, tangents.Item1);
					spline.SetRightTangent(i, tangents.Item2);
				}

				// TODO: update shadow caster
				// TODO: function?
				if (wall.TryGetComponent(out ShadowCaster2D shadowCaster))
				{
					shadowCaster.NonpublicSetterWorkaround("m_ShapePath", points);
					shadowCaster.NonpublicSetterWorkaround("m_ShapePathHash", points.GetHashCode());
				}
			}
		}

		// finalize children
		// NOTE that this has to be BEFORE spawning ladders in order to ensure all cutbacks are opened first
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			if (doorwayInfo.ChildRoom != null)
			{
				doorwayInfo.ChildRoom.FinalizeRecursive(ref npcDepth);
			}
		}

		// spawn any one-way locks/destructibles that aren't already done
		// TODO: unify w/ spawn logic during cutback creation?
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			if (doorwayInfo.m_onewayBlockageType == DoorwayInfo.BlockageType.Destructible)
			{
				if (doorwayInfo.m_blocker != null)
				{
					continue;
				}

				RoomController shallowRoom = doorwayInfo.SiblingShallowerRoom != null ? doorwayInfo.SiblingShallowerRoom : this;
				int dummyLockIdx = -1;
				bool noLadder = shallowRoom.SpawnGate(shallowRoom != this ? doorwayInfo : doorwayInfo.m_infoReverse, LayoutGenerator.Node.Type.Lock, doorwayInfo.m_excludeSelf.Value ? 0 : 1, 0.0f, ref dummyLockIdx, true, false); // NOTE the "reversed" DoorwayInfos to place the gate in deepRoom but as a child of shallowRoom, for better shadowing
				if (noLadder)
				{
					doorwayInfo.m_onewayBlockageType = DoorwayInfo.BlockageType.NoLadder; // NOTE that it's okay if we overwrite an existing value of Destructible; NoLadder should take priority since there should never be a situation where a ladder is dynamically spawned in a doorway that also has a one-way destructible
				}
			}
		}

		// spawn ladders and mark non-laddered upward doorways as one-way
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			if (doorwayInfo.m_onewayBlockageType == DoorwayInfo.BlockageType.NoLadder || doorwayInfo.ConnectedRoom == null || doorwayInfo.DirectionOutward().y <= 0.0f)
			{
				continue;
			}

			GameObject ladder = SpawnLadder(doorwayInfo.m_object);

			if (ladder != null)
			{
				fillPct += ChildBounds(ladder).extents.x / extentsInterior.x;
			}
			else
			{
				doorwayInfo.m_onewayBlockageType = DoorwayInfo.BlockageType.NoLadder; // NOTE that it's okay if we overwrite an existing value of Destructible; NoLadder should take priority since there should never be a situation where a ladder is dynamically spawned in a doorway that also has a one-way destructible
			}
		}

		// spawn locks
		// NOTE that this is done before flexible node architecture since some locks (e.g. ladders) have required positioning
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			SpawnKeys(doorwayInfo, (unlockable, lockRoom, keyRooms, difficultyPct) => unlockable.SpawnKeysStatic(lockRoom, keyRooms, difficultyPct)); // NOTE that this has to be before furniture to ensure space w/o overlap
		}

		// spawn flexible node architecture
		foreach (LayoutGenerator.Node node in LayoutNodes)
		{
			switch (node.m_type)
			{
				case LayoutGenerator.Node.Type.TutorialPlatforms:
				case LayoutGenerator.Node.Type.TutorialMove:
				case LayoutGenerator.Node.Type.TutorialAim:
				case LayoutGenerator.Node.Type.TutorialJump:
				case LayoutGenerator.Node.Type.TutorialInteract:
				case LayoutGenerator.Node.Type.TutorialUse:
				case LayoutGenerator.Node.Type.TutorialSwap:
				case LayoutGenerator.Node.Type.TutorialThrow:
				case LayoutGenerator.Node.Type.TutorialInventory:
				case LayoutGenerator.Node.Type.TutorialSwing:
				case LayoutGenerator.Node.Type.TutorialCancel:
				case LayoutGenerator.Node.Type.TutorialCatch:
				case LayoutGenerator.Node.Type.TutorialPassThrough:
				case LayoutGenerator.Node.Type.TutorialLook:
				case LayoutGenerator.Node.Type.TutorialDrop:
				case LayoutGenerator.Node.Type.TutorialDash:
				case LayoutGenerator.Node.Type.TutorialDashDamage:
				case LayoutGenerator.Node.Type.TutorialWallJump:
					RoomType.DecorationInfo info = GameController.Instance.m_textPrefabs[node.m_type - LayoutGenerator.Node.Type.TutorialPlatforms];
					GameObject prefab = info.m_prefabs.RandomWeighted();
					Quaternion rotation = info.m_rotationDegreesMax == 0.0f ? Quaternion.identity : Quaternion.Euler(0.0f, 0.0f, Random.Range(-info.m_rotationDegreesMax, info.m_rotationDegreesMax));
					Instantiate(prefab, InteriorPosition(info.m_heightMin, info.m_heightMax, prefab, rotation), rotation, transform);
					if (node.m_type == LayoutGenerator.Node.Type.TutorialInteract && (GameController.Instance.m_avatars == null || !GameController.Instance.m_avatars.Any(avatar => avatar.GetComponentInChildren<ItemController>(true) != null)))
					{
						foreach (WeightedObject<GameObject> item in RoomType.m_itemPrefabs)
						{
							GameController.Instance.m_savableFactory.Instantiate(item.m_object, InteriorPosition(0.0f), Quaternion.identity); // TODO: spawn on table
						}
					}
					break;

				case LayoutGenerator.Node.Type.ExitDoor1:
				case LayoutGenerator.Node.Type.ExitDoor2:
				case LayoutGenerator.Node.Type.ExitDoor3:
				case LayoutGenerator.Node.Type.ExitDoor4:
				case LayoutGenerator.Node.Type.ExitDoor5:
					fillPct += SpawnDoor(1 + node.m_type - LayoutGenerator.Node.Type.ExitDoor1, false, extentsInterior.x); // +1 due to entrance doors
					break;

				case LayoutGenerator.Node.Type.Npc:
				case LayoutGenerator.Node.Type.NpcGroup:
					int sceneIdx = GameController.Instance.SceneIndexEffective;
					int numToSpawn = node.m_type == LayoutGenerator.Node.Type.NpcGroup ? GameController.ZonesFinishedCount - npcDepthLocal + (GameController.Instance.NpcsTotal - GameController.m_zoneCount) : (npcDepthLocal + GameController.ZonesFinishedCount >= sceneIdx ? 0 : 1);
					for (int npcI = 0; npcI < numToSpawn; ++npcI)
					{
						AIController npcPrefab = GameController.Instance.m_npcPrefabs.RandomWeighted();
						InteractNpc npc = Instantiate(npcPrefab, InteriorPosition(0.0f) + (Vector3)npcPrefab.gameObject.OriginToCenterY(), Quaternion.identity).GetComponent<InteractNpc>();
						npc.Index = npcDepthLocal + sceneIdx;
						++npcDepthLocal;
						GameController.Instance.NpcAdd(npc.GetComponent<AIController>());
					}
					break;

				case LayoutGenerator.Node.Type.Enemy:
					// NOTE that this enemy won't be included in GameController.m_{waveEnemies/enemySpawnCounts}[] until room is opened and pathfinding succeeds
					AIController enemyPrefab = GameController.Instance.m_enemyPrefabs.Where(enemyObj => enemyObj.m_object.m_difficulty <= GameController.Instance.m_waveStartWeight).RandomWeighted();
					AIController enemy = Instantiate(enemyPrefab, InteriorPosition(0.0f) + (Vector3)enemyPrefab.gameObject.OriginToCenterY(), Quaternion.identity);
					GameController.Instance.EnemyAdd(enemy);
					break;

				case LayoutGenerator.Node.Type.Upgrade:
					if (GameController.Instance.m_zoneFinishedIndicators.Length <= 0)
					{
						Debug.LogWarning("No assigned zone finished indicators?");
						break;
					}
					GameObject prefabInitial = GameController.Instance.m_zoneFinishedIndicators.Random();
					InteractUpgrade[] upgradeInitial = new[] { Instantiate(prefabInitial, InteriorPosition(0.0f, prefabInitial), Quaternion.identity, transform).GetComponent<InteractUpgrade>() };
					upgradeInitial.First().ToggleActivation(true);

					GameObject[] upgradesRandom = GameController.Instance.m_upgradeIndicators.OrderBy(obj => Random.value).ToArray();
					const int numChoices = 2; // TODO: vary?
					for (int i = 0; i < numChoices; ++i)
					{
						GameObject prefabI = upgradesRandom.ElementAt(i);
						InteractUpgrade upgradeI = Instantiate(prefabI, InteriorPosition(0.0f, prefabI), Quaternion.identity, transform).GetComponent<InteractUpgrade>();
						upgradeI.m_sources = upgradeInitial;
					}
					break;

				case LayoutGenerator.Node.Type.FinalHint:
				case LayoutGenerator.Node.Type.FinalHintSequence:
					GameObject hintPrefab = GameController.Instance.m_hintPrefabs.RandomWeighted();
					Bounds placementBounds = BoundsInterior;
					Vector3 spawnPos = InteriorPosition(float.MaxValue, hintPrefab); // NOTE that we don't use edgeBuffer to ensure the whole sequence can fit since the total width can be wide enough to exclude all vertical placements in some rooms
					float width = 0.0f;
					int sceneIdxEffective = GameController.Instance.SceneIndexEffective;
					List<GameObject> spawnedHints = new();
					for (int i = 0; i < (node.m_type == LayoutGenerator.Node.Type.FinalHintSequence ? GameController.m_narrowPathLength : 1); ++i)
					{
						if (!placementBounds.Contains(spawnPos + new Vector3(width, 0.0f)))
						{
							spawnPos.x -= width;
							foreach (GameObject obj in spawnedHints)
							{
								obj.transform.position = new(obj.transform.position.x - width, obj.transform.position.y); // TODO: handle rooms too narrow to fit entire sequence?
							}
						}
						GameObject hintObj = Instantiate(hintPrefab, spawnPos, Quaternion.identity);
						Color hintColor = (!GameController.Instance.m_isHubScene || (i % 2 == 0 ? GameController.SecretFound(i / 2) : GameController.ZonesFinishedCount > i / 2)) ? GameController.NarrowPathColors[GameController.m_hintsPerZone * System.Math.Max(0, sceneIdxEffective - 1) + GameController.Instance.NarrowPathHintCount] : Color.black; // TODO: guarantee order of hints spawned?
						foreach (SpriteRenderer r in hintObj.GetComponentsInChildren<SpriteRenderer>())
						{
							r.color = hintColor;
							width = Mathf.Max(width, r.bounds.size.x);
						}
						spawnedHints.Add(hintObj);
						++GameController.Instance.NarrowPathHintCount;
						spawnPos.x += width;
						hintPrefab = GameController.Instance.m_hintPrefabs.RandomWeighted();
					}
					break;

				default:
					break;
			}
		}

		// shared furniture processing since decorations can end up containing furniture, too
		List<System.Tuple<FurnitureController, IUnlockable>> furnitureList = new();
		float processNewFurniture(FurnitureController furniture, float width)
		{
			IUnlockable furnitureLock = furniture.GetComponent<IUnlockable>();
			bool isLocked = furnitureLock != null && Random.value < m_furnitureLockPct; // TODO: more deliberate choice?
			furnitureList.Add(System.Tuple.Create(furniture, isLocked ? furnitureLock : null));

			if (furnitureLock != null)
			{
				if (isLocked)
				{
					furnitureLock.SpawnKeysStatic(this, new[] { this }, 0.0f);
				}
				else
				{
					furnitureLock.Unlock(null, true);
				}
			}

			return width * 0.5f / extentsInterior.x;
		}

		// spawn furniture
		// NOTE that this has to be before keys to allow spawning them on furniture
		while (RoomType.m_furniturePrefabs.Length > 0 && fillPct < RoomType.m_fillPctMin)
		{
			FurnitureController furniture = Instantiate(RoomType.m_furniturePrefabs.RandomWeighted(), transform).GetComponent<FurnitureController>(); // NOTE that we have to spawn before placement due to potential size randomization
			Vector2 extentsEffective = extentsInterior * (1.0f - fillPct);
			float width = furniture.RandomizeSize(extentsEffective);
			Vector3 furniturePos = InteriorPosition(0.0f, furniture.gameObject, resizeAction: () => width = furniture.RandomizeSize(extentsEffective), failureAction: () =>
			{
				Simulation.Schedule<ObjectDespawn>().m_object = furniture.gameObject;
				furniture = null; // NOTE that this is why we assign to a temporary position variable rather than straight to furniture.transform.position
			});
			if (furniture == null)
			{
				break; // must have failed to find a valid placement position
			}
			furniture.transform.position = furniturePos; // NOTE that we have to delay assigning to furniture since failureAction can potentially nullify it
			fillPct += processNewFurniture(furniture, width);
		}

		// spawn interior decorations
		// TODO: prioritize by area? take fillPct into account?
		int numDecorations = Random.Range(RoomType.m_decorationsMin, RoomType.m_decorationsMax + 1);
		float[] decorationTypeHeights = Enumerable.Repeat(float.MinValue, RoomType.m_decorations.Length).ToArray(); // TODO: allow similar decoration types to share heights? share between rooms?
		float roomHeight = extentsInterior.y * 2.0f;
		for (int i = 0; i < numDecorations; ++i)
		{
			RoomType.DecorationInfo decoInfo = RoomType.m_decorations.RandomWeighted();
			int decoIdx = System.Array.FindIndex(RoomType.m_decorations, weightedInfo => weightedInfo.m_object == decoInfo); // TODO: efficiency?
			float height = decoInfo.m_sharedHeight ? decorationTypeHeights[decoIdx] : float.MinValue;
			if (height == float.MinValue)
			{
				height = Random.Range(decoInfo.m_heightMin, Mathf.Min(roomHeight, decoInfo.m_heightMax));
				decorationTypeHeights[decoIdx] = height;
			}
			GameObject decoPrefab = decoInfo.m_prefabs.RandomWeighted();
			Quaternion rotation = decoInfo.m_rotationDegreesMax == 0.0f ? Quaternion.identity : Quaternion.Euler(0.0f, 0.0f, Random.Range(-decoInfo.m_rotationDegreesMax, decoInfo.m_rotationDegreesMax));
			Vector3 spawnPos = InteriorPosition(height, height, decoPrefab, rotation, failureAction: () => decoPrefab = null); // TODO: expand resizeAction to allow updating rotation?
			if (decoPrefab == null)
			{
				continue; // must not have found a valid placement position
			}
			GameObject decoration = Instantiate(decoPrefab, spawnPos, rotation, transform);

			foreach (SpriteRenderer renderer in decoration.GetComponentsInChildren<SpriteRenderer>(true))
			{
				if (renderer.GetComponent<ItemController>() != null)
				{
					continue;
				}
				renderer.flipX = Random.Range(0, 2) != 0;
			}

			foreach (FurnitureController decoFurniture in decoration.GetComponentsInChildren<FurnitureController>(true))
			{
				fillPct += processNewFurniture(decoFurniture, ChildBounds(decoration).size.x);
			}
		}

		// spawn items
		int itemCount = 0;
		List<GameObject> prefabsSpawned = new();
		int furnitureRemaining = furnitureList.Count - 1;
		foreach (System.Tuple<FurnitureController, IUnlockable> furniture in furnitureList)
		{
			itemCount += furniture.Item1.SpawnItems(furniture.Item2 != null || LayoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.BonusItems), RoomType, itemCount, furnitureRemaining, prefabsSpawned).Count;
			--furnitureRemaining;

			furniture.Item2?.SpawnKeysDynamic(this, new[] { this }, 0.0f); // TODO: spaced-out keys?
		}

		// spawn keys
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			SpawnKeys(doorwayInfo, (unlockable, lockRoom, keyRooms, difficultyPct) => unlockable.SpawnKeysDynamic(lockRoom, keyRooms, difficultyPct)); // NOTE that this has to be after furniture for item key placement
		}

		// spawn enemy spawn points
		m_spawnPoints = RoomType.m_spawnPointsMax <= 0 ? null : new GameObject[Random.Range(1, RoomType.m_spawnPointsMax + 1)]; // TODO: base on room size?
		for (int spawnIdx = 0, n = m_spawnPoints == null ? 0 : m_spawnPoints.Length; spawnIdx < n; ++spawnIdx)
		{
			GameObject spawnPrefab = m_spawnPointPrefabs.RandomWeighted();
			Quaternion rotation = Quaternion.Euler(0.0f, 0.0f, Random.Range(0.0f, 360.0f));
			Vector3 spawnPos = InteriorPosition(m_spawnPointHeightMax, spawnPrefab, rotation); // NOTE that we don't account for maximum enemy size, relying upon KinematicObject's checks to prevent getting stuck in walls
			m_spawnPoints[spawnIdx] = Instantiate(spawnPrefab, spawnPos, rotation, transform);
		}
	}

	public void FinalizeTopDown(float widthIncrement)
	{
		// TODO: early-out if no windows/lookouts have been spawned?

		// shared logic
		float widthIncrementHalf = widthIncrement * 0.5f;
		int layerMask = GameController.Instance.m_layerWalls | GameController.Instance.m_layerExterior;
		void trySpawningExteriorDecoration(WeightedObject<GameObject>[] prefabs, Vector3 position, float heightOverride, float verticalScale, System.Func<int, Vector3[]> posFunc = null, System.Func<Spline, Vector3, int, int, bool, System.Tuple<Vector3, Vector3>> tangentFunc = null, bool groundCheck = false)
		{
			bool hasSpace = verticalScale == 0.0f ? position.y >= 0.0f : position.y > 0.0f && !Physics2D.OverlapArea(position + new Vector3(-widthIncrementHalf + m_physicsCheckEpsilon, verticalScale * m_physicsCheckEpsilon), position + new Vector3(widthIncrementHalf - m_physicsCheckEpsilon, verticalScale), layerMask); // TODO: more nuanced height check?
			if (!hasSpace)
			{
				return;
			}

			GameObject obj = Instantiate(prefabs.RandomWeighted(), position, transform.rotation, transform);

			if (obj.TryGetComponent(out SpriteRenderer renderer))
			{
				renderer.size = new(widthIncrement, heightOverride >= 0.0f ? heightOverride : renderer.size.y);
			}

			if (posFunc != null || tangentFunc != null)
			{
				if (obj.TryGetComponent(out SpriteShapeController shape))
				{
					Spline spline = shape.spline;
					int nInitial = spline.GetPointCount();
					int n = Random.Range(nInitial, 13); // TODO: parameterize max?

					// set positions
					if (posFunc != null)
					{
						Vector3[] positions = posFunc(n);
						for (int i = 0; i < n; ++i)
						{
							Vector3 posLocal = positions[i];
							if (posLocal.y + position.y < 0.0f) // TODO: parameterize / move into posFunc()?
							{
								posLocal.y = -position.y;
							}
							if (i < nInitial)
							{
								spline.SetPosition(i, posLocal);
							}
							else
							{
								spline.InsertPointAt(i, posLocal); // TODO: prevent rare too-close-to-neighbor assert
								spline.SetTangentMode(i, ShapeTangentMode.Continuous); // TODO: parameterize / move into tangentFunc()?
							}
						}
					}

					// set tangents
					if (tangentFunc != null)
					{
						for (int i = 0; i < n; ++i)
						{
							System.Tuple<Vector3, Vector3> tangents = tangentFunc(spline, position, i, n, groundCheck);
							spline.SetLeftTangent(i, tangents.Item1);
							spline.SetRightTangent(i, tangents.Item2);
						}
					}
				}
			}

			if (obj.TryGetComponent(out BoxCollider2D collider))
			{
				collider.size = new(widthIncrement, heightOverride >= 0.0f ? heightOverride : renderer != null ? renderer.size.y : collider.size.y);
				collider.offset = new(collider.offset.x, collider.size.y * verticalScale * 0.5f);
			}
		}

		// try to spawn exterior decorations
		// TODO: more deliberate choices? combine adjacent instantiations when possible?
		for (int i = 0, n = Mathf.RoundToInt(Bounds.size.x / widthIncrement); i < n; ++i)
		{
			// above
			Vector3 exteriorPos = new(Bounds.min.x + widthIncrementHalf + widthIncrement * i, Bounds.max.y, Bounds.center.z);
			trySpawningExteriorDecoration(m_exteriorPrefabsAbove, exteriorPos, -1.0f, 1.0f);

			// within
			// TODO: support multiple per width increment?
			exteriorPos.y = Bounds.center.y;
			float radiusMax = Mathf.Min(widthIncrementHalf, Bounds.extents.y);
			trySpawningExteriorDecoration(m_exteriorPrefabsWithin, exteriorPos + new Vector3(Random.Range(-widthIncrementHalf, widthIncrementHalf), Random.Range(-Bounds.extents.y, Bounds.extents.y)), -1.0f, 0.0f, countMax => Enumerable.Range(0, countMax).Select(i => Quaternion.Euler(0.0f, 0.0f, i * -360.0f / countMax) * Vector3.right * Random.Range(1.0f, radiusMax)).ToArray(), TangentsFromSpline); // NOTE the negative rotation direction since SpriteShapes expect clockwise rotation for calculating outward-facing tangents // TODO: more radius continuity

			// on ground
			if (transform.position.y == 0.0f)
			{
				exteriorPos.y = 0.0f;
				float extentXPerPoint = Random.Range(0.5f, 1.0f); // TODO: parameterize?
				trySpawningExteriorDecoration(m_exteriorPrefabsOnGround, exteriorPos + new Vector3(Random.Range(-widthIncrementHalf, widthIncrementHalf), 0.0f), -1.0f, 0.0f, countMax =>
				{
					float extentX = extentXPerPoint * countMax;
					return HeightSplinePoints(countMax, false, false, false, -extentX, extentX, extentX, Bounds.size.y, 0.5f); // TODO: vary/derive more of the arguments?
				}, TangentsFromSpline, true);
			}

			// below
			exteriorPos.y = Bounds.min.y;
			RaycastHit2D raycast = Physics2D.Raycast(exteriorPos + Vector3.down * m_physicsCheckEpsilon, Vector2.down, transform.position.y, layerMask);
			trySpawningExteriorDecoration(m_exteriorPrefabsBelow, exteriorPos, raycast.distance == 0.0f ? transform.position.y : raycast.distance + m_physicsCheckEpsilon, -1.0f);
		}
	}

	public RoomController FromPosition(Vector2 position)
	{
		RoomController parentRoom = m_doorwayInfos.Select(info => info.ParentRoom).FirstOrDefault(parent => parent != null);
		if (parentRoom != null)
		{
			// traverse to root room
			return parentRoom.FromPosition(position);
		}
		else
		{
			// find matching room from descendants
			Vector3 pos3D = position;
			pos3D.z = Bounds.center.z; // TODO: don't assume that all rooms share depth?
			return FromPositionInternal(pos3D);
		}
	}

	public Vector3 InteriorPosition(float heightMax, GameObject preventOverlapPrefab = null, Quaternion? rotation = null, float edgeBuffer = 0.0f, System.Action resizeAction = null, System.Action failureAction = null, float xPreferred = float.NaN)
	{
		return InteriorPosition(0.0f, heightMax, preventOverlapPrefab, rotation, edgeBuffer, resizeAction, failureAction, xPreferred);
	}

	public Vector3 InteriorPosition(float heightMin, float heightMax, GameObject preventOverlapPrefab = null, Quaternion? rotation = null, float edgeBuffer = 0.0f, System.Action resizeAction = null, System.Action failureAction = null, float xPreferred = float.NaN)
	{
		static float getOverlap(Bounds a, Bounds b)
		{
			Vector2 centerDiff = b.center - a.center;
			Vector2 overlap2D = (Vector2)b.extents + (Vector2)a.extents - centerDiff.Abs();
			return Mathf.Min(overlap2D.x, overlap2D.y);
		}

		Bounds boundsInteriorOrig = BoundsInterior;
		if (edgeBuffer != 0.0f)
		{
			boundsInteriorOrig.Expand(new Vector3(-edgeBuffer, -edgeBuffer));
		}

		Vector3 pos = Vector3.zero;
		Vector3 posBackup = pos;
		float backupOverlap = float.MaxValue;
		const int attemptsMax = 100;
		int failsafe = attemptsMax;
		do // TODO: efficiency?
		{
			// calculate overlap bbox
			Bounds bboxNew = new();
			if (preventOverlapPrefab != null)
			{
				bboxNew = ChildBounds(preventOverlapPrefab, rotation: rotation);
				bboxNew.Expand(new Vector3(-Utility.FloatEpsilon, -Utility.FloatEpsilon, float.MaxValue)); // NOTE the slight x/y contraction to avoid always collecting the floor when up against it
			}

			// ensure overlap object fits entirely inside room bounds
			Bounds boundsInterior = boundsInteriorOrig;
			boundsInterior.Expand(-bboxNew.size);
			if (boundsInterior.extents.x < 0.0f)
			{
				resizeAction?.Invoke();
				continue;
			}

			// construct initial position
			float xInitial = float.IsNaN(xPreferred) ? boundsInterior.center.x + Random.Range(-boundsInterior.extents.x, boundsInterior.extents.x) : Mathf.Clamp(xPreferred, boundsInterior.min.x, boundsInterior.max.x);
			float yMaxFinal = Mathf.Min(heightMax, boundsInterior.size.y); // TODO: also count furniture surfaces as "floor"
			pos = new(xInitial, transform.position.y + Random.Range(heightMin, yMaxFinal), transform.position.z); // NOTE the assumptions that the object position is on the floor of the room but not necessarily centered
			if (preventOverlapPrefab == null)
			{
				return pos;
			}

			// get points until no overlap
			// TODO: more deliberate iteration? avoid tendency to line up in a row?
			Vector3 offsetToCenter = bboxNew.center - preventOverlapPrefab.transform.position; // NOTE that we can't assume the bbox is centered
			int xDir = (float.IsNaN(xPreferred) ? Random.value > 0.5f : xPreferred > boundsInterior.center.x) ? -1 : 1;
			float xWrap = boundsInterior.size.x * xDir;
			float xStep = xWrap / attemptsMax;
			int moveCount = attemptsMax;
			do
			{
				bboxNew.center = pos + offsetToCenter;

				// calculate overlap
				float overlap = 0.0f;
				foreach (Renderer renderer in GetComponentsInChildren<Renderer>().Where(r => r is SpriteRenderer or SpriteMask or MeshRenderer)) // NOTE that we would just exclude {Trail/VFX}Renderers except that VFXRenderer is inaccessible...
				{
					if (renderer.gameObject == m_backdrop || renderer.gameObject.layer == GameController.Instance.m_layerExterior.ToIndex())
					{
						continue;
					}
					if (renderer.bounds.Intersects(bboxNew))
					{
						overlap = Mathf.Max(overlap, getOverlap(renderer.bounds, bboxNew));
					}
					if (renderer.TryGetComponent(out RectTransform tf))
					{
						Bounds bboxRect = new((Vector3)tf.rect.center + tf.position, tf.rect.size);
						if (bboxRect.Intersects(bboxNew))
						{
							overlap = Mathf.Max(overlap, getOverlap(bboxRect, bboxNew));
						}
					}
				}
				if (overlap <= 0.0f)
				{
					return pos;
				}

				// track best backup position
				if (overlap < backupOverlap)
				{
					posBackup = pos;
					backupOverlap = overlap;
				}

				// iterate
				pos.x += xStep;
				pos.y = transform.position.y + Random.Range(heightMin, yMaxFinal);
				if (xDir > 0 ? pos.x > boundsInterior.max.x : pos.x < boundsInterior.min.x)
				{
					pos.x -= xWrap;
				}
			}
			while (--moveCount > 0);

			resizeAction?.Invoke();
		}
		while (resizeAction != null && --failsafe > 0);

		if (failureAction != null)
		{
			failureAction();
		}
		else
		{
			Debug.LogWarning("Failed to prevent room position overlap: " + preventOverlapPrefab.name + " at " + posBackup);
		}

		return posBackup;
	}

	public GameObject SpawnKey(GameObject prefab, float nonitemHeightMax, bool noLock, bool isCriticalPath)
	{
		bool isItem = prefab.GetComponent<Rigidbody2D>() != null; // TODO: ignore non-dynamic bodies?
		Vector3 spawnPos = isItem ? Vector3.zero : InteriorPosition(nonitemHeightMax, prefab); // TODO: prioritize placing non-items close to self if multiple in this room?
		GameObject keyObj = null;
		if (isItem)
		{
			IEnumerable<FurnitureController> validFurniture = transform.GetComponentsInChildren<FurnitureController>(true);
			if (noLock)
			{
				// TODO: encourage keys w/i locks as long as no cycles form?
				validFurniture = validFurniture.Where(furniture =>
				{
					IUnlockable furnitureLock = furniture.GetComponent<IUnlockable>();
					return furnitureLock == null || !furnitureLock.IsLocked;
				});
			}
			if (validFurniture.Count() <= 0)
			{
				spawnPos = InteriorPosition(0.0f) + (Vector3)prefab.OriginToCenterY();
			}
			else
			{
				FurnitureController chosenFurniture = validFurniture.Random(); // TODO: prioritize based on furniture type / existing items?
				keyObj = chosenFurniture.SpawnKey(prefab, isCriticalPath);
			}
		}
		if (keyObj == null)
		{
			keyObj = prefab.GetComponent<ISavable>() == null ? Instantiate(prefab, spawnPos, Quaternion.identity, isItem ? null : transform) : GameController.Instance.m_savableFactory.Instantiate(prefab, spawnPos, Quaternion.identity);
		}

		if (keyObj.TryGetComponent(out ItemController item))
		{
			item.IsCriticalPath = isCriticalPath;
		}

		return keyObj;
	}

	public Vector3 SpawnPointRandom() => (m_spawnPoints == null || m_spawnPoints.Length <= 0 ? gameObject : m_spawnPoints.Random()).transform.position;

	public System.Tuple<List<Vector2>, float> PositionPath(GameObject start, GameObject end, PathFlags flags = PathFlags.None, float extentY = -1.0f, float upwardMax = float.MaxValue, Vector2 offsetMag = default, float incrementDegrees = -1.0f)
	{
		AStarPath roomPath = RoomPath(start, end, flags, extentY, upwardMax, incrementDegrees);
		if (roomPath == null)
		{
			// TODO: find path to closest reachable point instead?
			return null;
		}
		List<Vector2> waypointPath = roomPath.m_pathPositions;
		Debug.Assert(waypointPath.Count >= 2);

		// offset end point
		// TODO: allow offset to cross room edges?
		if (offsetMag != Vector2.zero)
		{
			Vector2 endPosPreoffset = waypointPath.Last();
			float semifinalX = waypointPath[^2].x;
			Vector2 endPos = endPosPreoffset + (semifinalX >= endPosPreoffset.x ? offsetMag : new(-offsetMag.x, offsetMag.y));
			Bounds endRoomBounds = roomPath.m_pathRooms.Last().Bounds;
			endRoomBounds.Expand(new Vector3(-1.0f, -1.0f)); // TODO: dynamically determine wall thickness?
			waypointPath[^1] = endRoomBounds.Contains(new(endPos.x, endPos.y, endRoomBounds.center.z)) ? endPos : endRoomBounds.ClosestPoint(endPos); // TODO: flip offset if closest interior point is significantly different from endPos?
		}

		if (incrementDegrees > 0.0f)
		{
			RestrictAngleTo(waypointPath, waypointPath.Count - 1, incrementDegrees * Mathf.Deg2Rad);
		}

		return System.Tuple.Create(waypointPath, roomPath.m_distanceTotalEst);
	}

	public RoomController SpawnChildRoom(RoomController roomPrefab, LayoutGenerator.Node[] layoutNodes, Vector2[] allowedDirections, ref int orderedLockIdx)
	{
		// prevent putting keys behind their lock
		// NOTE that we check all nodes' depth even though all nodes w/i a single room should be at the same depth
		if (layoutNodes.Max(node => node.Depth) < LayoutNodes.Min(node => node.Depth))
		{
			return null;
		}

		// ensure areas end up grouped under a single room rather than spread out in different directions
		bool isSameArea = LayoutNodes.First().AreaParents == layoutNodes.First().AreaParents; // NOTE the assumption that all nodes w/i a single room share an area
		RoomController areaHeadRoom = isSameArea ? null : m_doorwayInfos.Select(info => info.ChildRoom).FirstOrDefault(childRoom => childRoom != null && childRoom.LayoutNodes.First().AreaParents == layoutNodes.First().AreaParents);
		if (areaHeadRoom != null)
		{
			return areaHeadRoom.SpawnChildRoom(roomPrefab, layoutNodes, allowedDirections, ref orderedLockIdx);
		}

		int orderedLockIdxTmp = orderedLockIdx; // due to not being able to use an outside reference inside a lambda
		RoomController childRoom = DoorwaysRandomOrder(doorway =>
		{
			if (doorway.ConnectedRoom != null)
			{
				return null;
			}

			// maybe replace/remove
			MaybeReplaceDoor(doorway, roomPrefab, layoutNodes, allowedDirections, ref orderedLockIdxTmp);
			return doorway.ChildRoom;
		});
		orderedLockIdx = orderedLockIdxTmp;

		if (childRoom != null)
		{
			return childRoom;
		}

		// any rooms requiring direct parent-child connection should early-out here
		if (layoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomIndefinite || node.m_type == LayoutGenerator.Node.Type.RoomIndefiniteCorrect))
		{
			return null;
		}

		// try spawning from children
		RoomController newRoom = DoorwaysRandomOrder(doorway =>
		{
			if (doorway.ChildRoom == null)
			{
				return null;
			}
			return doorway.ChildRoom.SpawnChildRoom(roomPrefab, layoutNodes, allowedDirections, ref orderedLockIdxTmp);
		});
		orderedLockIdx = orderedLockIdxTmp;
		return newRoom;
	}

	public GameObject SpawnLadder(GameObject doorway, LadderInfo prefabForced = null, bool spawnBunched = false)
	{
#if DEBUG
		DoorwayInfo info = m_doorwayInfos.FirstOrDefault(info => info.m_object == doorway || info.m_blocker == doorway);
		Debug.Assert(info != null && !info.m_disallowLadders);
#endif

		WeightedObject<LadderInfo>[] allowedLadders = m_ladderRungPrefabs.CombineWeighted(GameController.Instance.m_ladderRungPrefabs, weightedInfo => weightedInfo.m_object.m_rungPrefab, weightedPrefab => weightedPrefab.m_object).ToArray();
		LadderInfo ladderRungInfo = prefabForced != null || allowedLadders.Length <= 0 ? prefabForced : allowedLadders.RandomWeighted();
		if (ladderRungInfo == null)
		{
			return null;
		}
		GameObject ladderRungPrefab = ladderRungInfo.m_rungPrefab;

		// determine rung count/height
		DistanceJoint2D firstJoint = ladderRungPrefab.GetComponent<DistanceJoint2D>(); // TODO: genericize?
		bool hanging = firstJoint != null; // TODO: move into LadderInfo?
		float yTop = doorway.transform.position.y - (hanging ? 0.0f : 1.5f); // TODO: base top distance on character jump height?
		float heightDiff = yTop - transform.position.y; // TODO: don't assume pivot point is always the place to stop?
		float rungOnlyHeight = ladderRungPrefab.GetComponent<SpriteRenderer>().size.y;
		float rungHeightTotal = hanging ? firstJoint.distance + rungOnlyHeight : rungOnlyHeight;
		int rungCount = ladderRungInfo.m_singleRung ? 1 : Mathf.RoundToInt(heightDiff / rungHeightTotal) - (hanging ? 1 : 0);
		if (!hanging)
		{
			rungHeightTotal = heightDiff / rungCount;
		}

		Vector3 posItr = ladderRungInfo.m_singleRung ? new(doorway.transform.position.x, transform.position.y, doorway.transform.position.z) : doorway.transform.position; // TODO: separate param for spawning at the base rather than the top?
		GameObject firstRung = null;
		bool postSpawnDrop = hanging && spawnBunched;
		posItr.y = yTop - (postSpawnDrop ? 0.0f : rungHeightTotal);
		Rigidbody2D bodyPrev = null;
		for (int i = 0; i < rungCount; ++i)
		{
			// create
			GameObject ladder = Instantiate(ladderRungPrefab, posItr, Quaternion.identity, transform);
			if (firstRung == null)
			{
				firstRung = ladder;
			}

			// connect to previous rung
			AnchoredJoint2D[] joints = ladder.GetComponents<AnchoredJoint2D>();
			foreach (AnchoredJoint2D joint in joints)
			{
				joint.connectedBody = bodyPrev;
				if (bodyPrev == null)
				{
					joint.connectedAnchor = (Vector2)joint.transform.position + new Vector2(joint.anchor.x, postSpawnDrop ? 0.0f : rungHeightTotal);
				}
			}
			bodyPrev = ladder.GetComponent<Rigidbody2D>();

			if (!hanging && !ladderRungInfo.m_singleRung)
			{
				// resize
				// NOTE that we adjust bottom-up ladders to ensure the top rung is within reach of any lock above it due to the way that combination locks used to work
				SpriteRenderer renderer = ladder.GetComponent<SpriteRenderer>();
				renderer.size = new(renderer.size.x, rungHeightTotal);
				BoxCollider2D collider = ladder.GetComponent<BoxCollider2D>();
				collider.size = new(collider.size.x, rungHeightTotal);
				collider.offset = new(collider.offset.x, rungHeightTotal * 0.5f);
			}

			if (ladder.TryGetComponent(out Spring spring))
			{
				spring.m_launchDistance = heightDiff + rungOnlyHeight;
			}

			// iterate
			posItr.x += Random.Range(-m_ladderRungSkewMax, m_ladderRungSkewMax); // TODO: guarantee AI navigability? clamp to room size?
			posItr.y -= spawnBunched ? rungOnlyHeight : rungHeightTotal;
		}

		DoorwayInfo doorwayInfo = m_doorwayInfos.First(info => info.m_object == doorway || info.m_blocker == doorway);
		Debug.Assert(doorwayInfo.m_onewayBlockageType != DoorwayInfo.BlockageType.Destructible);
		doorwayInfo.m_onewayBlockageType = DoorwayInfo.BlockageType.None;
		return firstRung;
	}

	public void LinkRecursive() => LinkRecursiveInternal(new());

	public void SealRoom(bool seal, bool isBossUnseal = false)
	{
		foreach (DoorwayInfo doorway in m_doorwayInfos)
		{
			const PathFlags flags = PathFlags.ObstructionCheck | PathFlags.Directional;
			if (doorway.ConnectedRoom == null || ((doorway.m_disallowLadders || m_ladderRungPrefabs.Length <= 0) && doorway.DirectionOutward() == Vector2.up) || (isBossUnseal && doorway.ParentRoom != null && RoomPath(doorway.Room.gameObject, doorway.ConnectedRoom.gameObject, flags) != null && doorway.ConnectedRoom.RoomPath(doorway.ConnectedRoom.gameObject, doorway.Room.gameObject, flags) != null)) // TODO: efficiency?
			{
				continue;
			}

			bool wasOpen = doorway.IsOpen();
			OpenDoorway(doorway, !seal);

			if (seal && wasOpen)
			{
				GameObject doorwayObj = doorway.m_object;
				Vector2 doorwaySize = doorway.Size();
				VisualEffect vfx = Instantiate(m_doorSealVFX.RandomWeighted(), doorwayObj.transform.position + new Vector3(0.0f, -0.5f * doorwaySize.y), Quaternion.identity);
				vfx.SetVector3("StartAreaSize", new(doorwaySize.x, 0.0f));

				doorwayObj.GetComponent<AudioSource>().Play();
				// TODO: animation?
			}
		}

		GameController.Instance.GetComponent<CompositeShadowCaster2D>().enabled = true; // NOTE that the top-level caster has to start disabled due to an assert from CompositeShadowCaster2D when empty of child casters
		GetComponent<CompositeShadowCaster2D>().enabled = seal;
		transform.SetParent(seal ? null : GameController.Instance.transform);
	}


	// callbacks for RoomType.m_preconditionName, called via FinalizeRecursive()/SendMessage()
	// TODO: variable preference factors?
	public void IsAboveGround(SendMessageValue<float> result) => result.m_out = transform.position.y >= 0.0f ? 1.0f : 0.0f; // NOTE that the ground floor is always at y=0
	public void IsBelowGround(SendMessageValue<float> result) => result.m_out = transform.position.y < 0.0f ? 1.0f : 0.0f; // NOTE that the ground floor is always at y=0
	public void IsTopFloor(SendMessageValue<float> result) => result.m_out = Physics2D.OverlapArea(new(Bounds.min.x + m_physicsCheckEpsilon, Bounds.max.y + m_physicsCheckEpsilon), new(Bounds.max.x - m_physicsCheckEpsilon, Bounds.max.y + 100.0f), GameController.Instance.m_layerWalls) == null ? 1.0f : 0.0f; // TODO: efficiency?
	public void PreferNonDeadEnds(SendMessageValue<float> result) => result.m_out = m_doorwayInfos.Any(info => info.ChildRoom != null || info.SiblingShallowerRoom != null || info.SiblingDeeperRoom != null) ? GameController.Instance.m_roomTypes.Length : 1.0f / GameController.Instance.m_roomTypes.Length;
	public void PreferDeadEnds(SendMessageValue<float> result) => result.m_out = m_doorwayInfos.Any(info => info.ChildRoom != null || info.SiblingShallowerRoom != null || info.SiblingDeeperRoom != null) ? 1.0f / GameController.Instance.m_roomTypes.Length : GameController.Instance.m_roomTypes.Length;
	public void PreferUpwardEntrance(SendMessageValue<float> result) => result.m_out = Parent != null && Parent.transform.position.y < transform.position.y ? GameController.Instance.m_roomTypes.Length : 1.0f / GameController.Instance.m_roomTypes.Length;


	private int[] DoorwayReverseIndices(Vector2 replaceDirection)
	{
		List<int> indices = new();
		for (int i = 0; i < m_doorwayInfos.Length; ++i)
		{
			DoorwayInfo doorwayInfo = m_doorwayInfos[i];
			Vector3 doorwaySize = doorwayInfo.Size();
			if (Vector2.Dot((Vector2)transform.position - (Vector2)doorwayInfo.m_object.transform.position, replaceDirection) > 0.0f && doorwaySize.x > doorwaySize.y == (Mathf.Abs(replaceDirection.x) < Mathf.Abs(replaceDirection.y))) // TODO: better way of determining reverse direction doorway?
			{
				indices.Add(i);
			}
		}
		return indices.ToArray();
	}

	private T DoorwaysRandomOrder<T>(System.Func<DoorwayInfo, T> f, bool prioritizeAdjacentRooms = true)
	{
		foreach (DoorwayInfo info in m_doorwayInfos.OrderBy(i => prioritizeAdjacentRooms ? 1.0f - Random.value * i.AdjacentRoomPct() : Random.value))
		{
			T result = f(info);
			if (result != null)
			{
				return result;
			}
		}
		return default;
	}

	private void OnObjectDespawn(ObjectDespawn evt)
	{
		DoorwayInfo doorwayInfo = m_doorwayInfos.FirstOrDefault(info => info.m_blocker == evt.m_object);
		if (doorwayInfo != null)
		{
			if (doorwayInfo.m_onewayBlockageType == DoorwayInfo.BlockageType.Destructible)
			{
				doorwayInfo.m_onewayBlockageType = DoorwayInfo.BlockageType.None;
			}
			if (doorwayInfo.IsOpen(true) && doorwayInfo.m_infoReverse.IsOpen(true))
			{
				LinkRecursive();
			}
		}
	}

	private void LinkRecursiveInternal(List<RoomController> visitedRooms)
	{
		Debug.Assert(!visitedRooms.Contains(this));
		visitedRooms.Add(this);

		// group this room's shadow casters under the top-level GameController caster
		GetComponent<CompositeShadowCaster2D>().enabled = false;
		transform.SetParent(GameController.Instance.transform);

		// runtime generation if requested
		const int runtimeRoomsMax = 10; // TODO: calculate based on hardware capabilities?
		bool isCorrect = LayoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomIndefiniteCorrect);
		if (m_doorwayInfos.All(info => info.ChildRoom == null) && (isCorrect || LayoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomIndefinite)))
		{
			if (!isCorrect)
			{
				GameController.Instance.OnNarrowPath = false;
			}

			int dummyIdx = GameController.Instance.m_doorInteractPrefabs.Length - 1; // TODO: don't assume that always/only the last door will spawn via runtime generation?
			List<LayoutGenerator.Node> newNodeTree = null;
			const int maxAttempts = 4; // NOTE that this corresponds to the maximum number of sideways/downward doors in a room // TODO: un-hardcode?
			for (int i = 0; i < maxAttempts; ++i)
			{
				// create and link nodes
				int depth = LayoutNodes.Max(node => node.Depth);
				if (newNodeTree == null)
				{
					isCorrect = isCorrect && i == 0 && GameController.Instance.OnNarrowPath;
					newNodeTree = new() { new(LayoutGenerator.Node.Type.Secret, new() { new(LayoutGenerator.Node.Type.TightCoupling, new() { new(isCorrect ? (depth > GameController.NarrowPathColors.Length ? LayoutGenerator.Node.Type.ExitDoor5 : LayoutGenerator.Node.Type.RoomIndefiniteCorrect) : LayoutGenerator.Node.Type.RoomIndefinite) }) }) }; // TODO: streamline?
					foreach (LayoutGenerator.Node node in LayoutNodes)
					{
						node.AddChildren(newNodeTree);
					}
				}

				// create room
				// TODO: better way of specifying gate color?
				const float colorDifferentiationEpsilon = 0.4f; // TODO: parameterize?
				Color colorCorrect = GameController.Instance.OnNarrowPath ? GameController.NarrowPathColors[System.Math.Clamp(depth - 2, 0, GameController.NarrowPathColors.Length - 1)] : Color.black; // TODO: better way of getting NarrowPathColors[] index?
				newNodeTree.First().AreaParents.First().m_room.m_wallColor = isCorrect ? colorCorrect : Utility.ColorRandom(Color.black, Color.white, false, colorDifferentiationEpsilon, colorCorrect);
				RoomController newRoom = SpawnChildRoom(GameController.Instance.m_roomPrefabs.RandomWeighted(), newNodeTree.SelectMany(node => node.WithDescendants).ToArray(), new[] { Vector2.left, Vector2.right, Vector2.down }, ref dummyIdx); // TODO: allow upward generation as long as it doesn't break through the ground?
				if (newRoom != null)
				{
					newRoom.FinalizeRecursive(ref dummyIdx);

					// add/replace room
					m_runtimeRooms.RemoveAll(room => room == null); // NOTE that this is so that we don't need to bother detecting scene load and clearing m_runtimeRooms[]
					if (m_runtimeRooms.Count >= runtimeRoomsMax)
					{
						RoomController roomToDelete = m_runtimeRooms.SelectMax(room => GameController.Instance.m_avatars.Min(avatar => (avatar.transform.position - room.Bounds.ClosestPoint(avatar.transform.position)).sqrMagnitude)); // TODO: prefer to avoid cutting off return path?

						foreach (DoorwayInfo doorway in roomToDelete.m_doorwayInfos)
						{
							if (doorway.m_infoReverse == null)
							{
								continue;
							}
							doorway.ConnectedRoom.OpenDoorway(doorway.m_infoReverse, false); // TODO: ensure m_wallColor is correct despite any previous manipulation to force gate color?
							if (doorway.m_infoReverse.m_blocker != null)
							{
								Simulation.Schedule<ObjectDespawn>().m_object = doorway.m_infoReverse.m_blocker;
							}
							doorway.m_infoReverse.m_infoReverse = null;
							if (doorway.ChildRoom != null)
							{
								GameController.Instance.AddRootRoom(doorway.ChildRoom);
							}
						}

						// TODO: despawn any non-child objects in room?

						foreach (LayoutGenerator.Node node in roomToDelete.LayoutNodes)
						{
							foreach (LayoutGenerator.Node parentNode in node.DirectParentsInternal)
							{
								parentNode.m_children.Remove(node);
							}
							if (node.m_children != null)
							{
								foreach (LayoutGenerator.Node childNode in node.m_children)
								{
									childNode.DirectParentsInternal.Remove(node);
								}
							}
						}

						Simulation.Schedule<ObjectDespawn>().m_object = roomToDelete.gameObject;

						m_runtimeRooms.Remove(roomToDelete);
					}
					m_runtimeRooms.Add(newRoom);

					newNodeTree = null;
				}
			}

			// cleanup if the last room was canceled
			if (newNodeTree != null)
			{
				foreach (LayoutGenerator.Node node in newNodeTree)
				{
					node.DirectParents.Remove(node);
				}
			}
		}

		// recurse into visible children
		foreach (DoorwayInfo info in m_doorwayInfos)
		{
			RoomController connectedRoom = info.ConnectedRoom;
			if (connectedRoom == null || visitedRooms.Contains(connectedRoom))
			{
				continue;
			}

			if (info.m_blocker == null || info.m_blocker.GetComponent<ShadowCaster2D>() == null) // NOTE that m_infoReverse.m_blocker should always be the same as m_blocker, so we only check one
			{
				connectedRoom.LinkRecursiveInternal(visitedRooms);
			}
		}
	}

	private static Bounds ChildBounds(GameObject obj, bool recursive = true, Quaternion? rotation = null)
	{
		Renderer[] renderers = obj.GetComponentsInChildren<Renderer>().Where(r => r is SpriteRenderer or SpriteMask or MeshRenderer).ToArray(); // NOTE that we would just exclude {Trail/VFX}Renderers except that VFXRenderer is inaccessible...
		Bounds SemiLocalBounds(Renderer r)
		{
			Bounds b = r.localBounds;
			b.extents = Vector3.Scale(b.extents, r.transform.lossyScale);
			b.center += r.transform.position - obj.transform.position;
			return b;
		}
		Bounds bboxNew = renderers.Length <= 0 ? new(obj.transform.position, Vector3.zero) : SemiLocalBounds(renderers.First()); // NOTE that we can't assume the local origin should always be included
		if (recursive)
		{
			foreach (Renderer renderer in renderers)
			{
				bboxNew.Encapsulate(SemiLocalBounds(renderer));
			}
		}
		RectTransform[] tfs = recursive ? obj.GetComponentsInChildren<RectTransform>() : obj.GetComponents<RectTransform>();
		foreach (RectTransform tf in tfs)
		{
			bboxNew.Encapsulate(new Bounds(tf.rect.center, tf.rect.size));
		}

		bboxNew.center += obj.transform.position;

		Quaternion rotationFinal = rotation != null ? rotation.Value : obj.transform.rotation;
		if (rotationFinal == Quaternion.identity)
		{
			return bboxNew;
		}
		return Utility.BoundsRotated(bboxNew, rotationFinal, true);
	}

	private LayoutGenerator.Node GateNodeToChild(LayoutGenerator.Node[] childNodes, params LayoutGenerator.Node.Type[] gateTypes)
	{
		IEnumerable<LayoutGenerator.Node> ancestors = LayoutNodes.Select(node => node.FirstCommonAncestor(childNodes)).Distinct();
		return ancestors.FirstOrDefault(node => gateTypes.Contains(node.m_type) && childNodes.Any(childNode => childNode.DirectParents.Contains(node))); // TODO: ensure gates are placed even if a room ends up between the gate and child rooms?
	}

	// TODO: inline?
	private void MaybeReplaceDoor(DoorwayInfo doorwayInfo, RoomController roomPrefab, LayoutGenerator.Node[] childNodes, Vector2[] allowedDirections, ref int orderedLockIdx)
	{
		Debug.Assert(m_doorwayInfos.Contains(doorwayInfo));
		Vector2 replaceDirection = doorwayInfo.DirectionOutward();
		if (replaceDirection.y > 0.0f && doorwayInfo.m_disallowLadders) // TODO: detect & allow situations that won't end up needing a ladder?
		{
			return;
		}
		if (allowedDirections != null && !allowedDirections.Contains(replaceDirection))
		{
			return;
		}

		GameObject doorway = doorwayInfo.m_object;
		Assert.IsNull(doorwayInfo.ConnectedRoom);
		Assert.AreApproximatelyEqual(replaceDirection.magnitude, 1.0f);

		// determine child position
		Bounds childBounds = roomPrefab.m_backdrop.GetComponent<SpriteRenderer>().bounds; // NOTE that we can't use Bounds since uninstantiated prefabs don't have Awake() called on them
		Vector2 doorwayPos = doorway.transform.position;
		int reverseIdx = -1;
		float doorwayToEdge = Mathf.Min(Bounds.max.x - doorwayPos.x, Bounds.max.y - doorwayPos.y, doorwayPos.x - Bounds.min.x, doorwayPos.y - Bounds.min.y); // TODO: don't assume convex rooms?
		Vector2 childPivotPos = Vector2.zero;
		Vector2 childPivotToCenter = childBounds.center - roomPrefab.transform.position;
		bool isOpen = false;
		foreach (int idxCandidate in roomPrefab.DoorwayReverseIndices(replaceDirection).OrderBy(i => Random.value))
		{
			Vector2 childDoorwayPosLocal = roomPrefab.m_doorwayInfos[idxCandidate].m_object.transform.position;
			float childDoorwayToEdge = Mathf.Min(childBounds.max.x - childDoorwayPosLocal.x, childBounds.max.y - childDoorwayPosLocal.y, childDoorwayPosLocal.x - childBounds.min.x, childDoorwayPosLocal.y - childBounds.min.y);
			childPivotPos = doorwayPos + replaceDirection * (doorwayToEdge + childDoorwayToEdge) - childDoorwayPosLocal;

			// check for obstructions
			isOpen = !Physics2D.OverlapBox(childPivotPos + childPivotToCenter, childBounds.size - new Vector3(m_physicsCheckEpsilon, m_physicsCheckEpsilon), 0.0f, GameController.Instance.m_layerWalls); // NOTE the small size reduction to avoid always collecting ourself
			if (isOpen)
			{
				reverseIdx = idxCandidate;
				break;
			}
		}
		if (!isOpen)
		{
			return;
		}

		if (replaceDirection.y < 0.0f && roomPrefab.m_doorwayInfos[reverseIdx].m_disallowLadders) // TODO: detect & allow situations that won't end up needing a ladder?
		{
			return;
		}

		RoomController childRoom = Instantiate(roomPrefab, childPivotPos, Quaternion.identity);
		doorwayInfo.ChildRoom = childRoom;
		DoorwayInfo reverseDoorwayInfo = childRoom.m_doorwayInfos[reverseIdx];
		doorwayInfo.m_infoReverse = reverseDoorwayInfo;
		reverseDoorwayInfo.ParentRoom = this;
		reverseDoorwayInfo.m_infoReverse = doorwayInfo;

		LayoutGenerator.Node blockerNode = GateNodeToChild(childNodes, LayoutGenerator.Node.Type.Lock, LayoutGenerator.Node.Type.LockOrdered, LayoutGenerator.Node.Type.GateBreakable, LayoutGenerator.Node.Type.Secret);
		if (blockerNode != null)
		{
			bool noLadder = SpawnGate(doorwayInfo, blockerNode.m_type, blockerNode.m_type != LayoutGenerator.Node.Type.Lock ? 0 : blockerNode.DirectParents.Count(node => node.m_type == LayoutGenerator.Node.Type.Key && (!doorwayInfo.m_excludeSelf.Value || node.m_room != this)), blockerNode.DepthPercent, ref orderedLockIdx, false, !blockerNode.IsOptional);
			if (noLadder)
			{
				doorwayInfo.m_onewayBlockageType = DoorwayInfo.BlockageType.NoLadder; // NOTE that it's okay if we overwrite an existing value of Destructible; NoLadder should take priority since there should never be a situation where a ladder is dynamically spawned in a doorway that also has a one-way destructible
			}
		}

		childRoom.SetNodes(childNodes);

		OpenDoorway(doorwayInfo, true);
		childRoom.OpenDoorway(reverseDoorwayInfo, true);
	}

	private float SpawnDoor(int doorwayDepth, bool isEntrance, float extentsInteriorX)
	{
		GameObject[] doorInteractPrefabs = GameController.Instance.m_doorInteractPrefabs;
		GameObject doorPrefab = doorInteractPrefabs[System.Math.Min(doorInteractPrefabs.Length - 1, doorwayDepth)];
		GameObject doorObj = Instantiate(doorPrefab, isEntrance ? transform.position : InteriorPosition(0.0f, 0.0f, doorPrefab), Quaternion.identity, transform);
		if (doorObj.TryGetComponent(out InteractScene doorInteract))
		{
			doorInteract.Depth = doorwayDepth;
		}
		return ChildBounds(doorInteract.gameObject).extents.x / extentsInteriorX;
	}

	private bool SpawnGate(DoorwayInfo doorwayInfo, LayoutGenerator.Node.Type type, int preferredKeyCount, float depthPct, ref int orderedLockIdx, bool isCutback, bool isCriticalPath)
	{
		DoorwayInfo reverseInfo = doorwayInfo.m_infoReverse;
		Debug.Assert(m_doorwayInfos.Contains(doorwayInfo) || m_doorwayInfos.Contains(reverseInfo));
		Vector2 replaceDirection = doorwayInfo.DirectionOutward();

		// determine gate type(s)
		bool isLock = type == LayoutGenerator.Node.Type.Lock;
		bool isOrdered = type == LayoutGenerator.Node.Type.LockOrdered;
		bool isSecret = type == LayoutGenerator.Node.Type.Secret;
		bool isOrderedOrSecret = isOrdered || isSecret;

		// filter allowed gates
		// TODO: disallow cutbacks w/ generic keys that already exist?
		DirectionalDoors[] directionalGates = m_doorDirectionalPrefabs.Where(pair => pair.m_direction == replaceDirection).ToArray(); // NOTE that even if we don't use this list to choose, we still check it for ladder suppression
		WeightedObject<GameObject>[] nondirectionalGates = isOrderedOrSecret ? null : isCutback ? m_cutbackPrefabs : m_gatePrefabs;
		IEnumerable<WeightedObject<GameObject>> gatesFinal = isOrdered ? null : isSecret ? m_doorSecretPrefabs : directionalGates != null ? directionalGates.SelectMany(gate => gate.m_prefabs).Concat(nondirectionalGates) : nondirectionalGates;
		gatesFinal = isOrdered ? null : gatesFinal.Where(weightedObj => isLock ? weightedObj.m_object.GetComponentInChildren<IUnlockable>() != null : weightedObj.m_object.GetComponentInChildren<IUnlockable>() == null).CombineWeighted(isCutback ? GameController.Instance.m_cutbackPrefabs : GameController.Instance.m_gatePrefabs);

		// pick & create gate
		GameObject blockerPrefab = isOrdered ? GameController.Instance.m_gateOrderedPrefabs[System.Math.Min(orderedLockIdx++, GameController.Instance.m_gateOrderedPrefabs.Length - 1)] : RandomWeightedByKeyCount(gatesFinal, ObjectToKeyStats, preferredKeyCount, depthPct);
		Debug.Assert(doorwayInfo.m_blocker == null);
		doorwayInfo.m_blocker = Instantiate(blockerPrefab, doorwayInfo.m_object.transform.position, Quaternion.identity, transform);

		// set references
		IUnlockable unlockable = doorwayInfo.m_blocker.GetComponentInChildren<IUnlockable>();
		if (unlockable != null)
		{
			unlockable.Parent = gameObject;
			unlockable.IsCriticalPath = isCriticalPath;
		}
		Debug.Assert(reverseInfo.m_blocker == null);
		reverseInfo.m_blocker = doorwayInfo.m_blocker;
		if (isCutback && isLock)
		{
			reverseInfo.m_onewayBlockageType = DoorwayInfo.BlockageType.Destructible;
		}

		// resize/recolor gate to fit doorway
		SpriteRenderer[] renderers = doorwayInfo.m_blocker.GetComponentsInChildren<SpriteRenderer>();
		Vector2 size = doorwayInfo.Size();
		int i = 0;
		foreach (SpriteRenderer renderer in renderers)
		{
			renderer.size = size;
			renderer.GetComponent<BoxCollider2D>().size = doorwayInfo.Room.Parent == null && replaceDirection == Vector2.down && doorwayInfo.m_object.transform.position.x == 0.0f ? size * 0.95f : size; // NOTE the workaround to avoid electric gates under the spawn point immediately damaging avatars after spawn // TODO: more intelligent placement? don't assume the spawn point is always in the root room at x=0?
			if (isSecret)
			{
				renderer.color = LayoutNodes.First().AreaParents.First().m_room.m_wallColor; // NOTE that in case m_wallColor isn't set yet FinalizeRecursive() will iterate over existing doorway blockers when m_wallColor is set
			}

			// TODO: don't assume that multi-part gates are synonymous w/ one-way breakable gates?
			if (i > 0)
			{
				renderer.transform.localPosition = replaceDirection * size * i;
			}
			if (renderers.Length > 1)
			{
				if (renderer.TryGetComponent(out Health health))
				{
					health.m_invincibilityDirection = replaceDirection;
				}
			}

			if (renderer.TryGetComponent(out VisualEffect vfx))
			{
				vfx.SetVector3("StartAreaSize", size);
			}

			// update shadow caster shape
			if (renderer.TryGetComponent(out ShadowCaster2D shadowCaster))
			{
				Vector3 extents = size * 0.5f;
				Vector3[] shapePath = new Vector3[] { new(-extents.x, -extents.y), new(extents.x, -extents.y), new(extents.x, extents.y), new(-extents.x, extents.y) };

				shadowCaster.NonpublicSetterWorkaround("m_ShapePath", shapePath);
				shadowCaster.NonpublicSetterWorkaround("m_ShapePathHash", shapePath.GetHashCode());
			}
			++i;
		}

		// determine whether to disallow ladders
		return directionalGates != null && directionalGates.FirstOrDefault(gates => gates.m_prefabs.Any(p => p.m_object == blockerPrefab)).m_suppressLadders; // TODO: handle duplicate entries?
	}

	private void SpawnKeys(DoorwayInfo doorwayInfo, System.Action<IUnlockable, RoomController, RoomController[], float> spawnAction)
	{
		Debug.Assert(m_doorwayInfos.Contains(doorwayInfo));

		if (doorwayInfo.ChildRoom == null && doorwayInfo.SiblingShallowerRoom == null)
		{
			return;
		}

		IUnlockable unlockable = doorwayInfo.m_blocker == null ? null : doorwayInfo.m_blocker.GetComponent<IUnlockable>();
		if (unlockable == null)
		{
			return;
		}

		// spawn keys
		LayoutGenerator.Node lockNode = doorwayInfo.ChildRoom == null ? null : GateNodeToChild(doorwayInfo.ChildRoom.LayoutNodes, LayoutGenerator.Node.Type.Lock, GameController.Instance.m_isHubScene ? (LayoutGenerator.Node.Type)(-1) : LayoutGenerator.Node.Type.LockOrdered); // NOTE that we ignore LockOrdered nodes in the hub since they don't spawn their own keys
		RoomController[] keyRooms = doorwayInfo.ChildRoom == null ? new[] { this } : (lockNode != null && lockNode.m_type == LayoutGenerator.Node.Type.LockOrdered) ? new[] { GameController.Instance.RoomFromPosition(Vector2.zero).WithDescendants.Where(r => r.LayoutNodes.Max(n => n.Depth) < LayoutNodes.Max(n => n.Depth)).Random() } : lockNode?.DirectParents.Where(node => node.m_type == LayoutGenerator.Node.Type.Key).Select(node => node.m_room).ToArray(); // TODO: efficiency? don't assume that all ordered locks should have a key placed randomly in any prior room?
		float depthPct = lockNode == null ? 0.0f : lockNode.DepthPercent;
		spawnAction(unlockable, this, keyRooms == null || keyRooms.Length <= 0 ? null : doorwayInfo.m_excludeSelf.Value ? keyRooms.Where(room => room != this).ToArray() : keyRooms, depthPct);
	}

	// TODO: combine height/tangent functions?
	private Vector3[] HeightSplinePoints(int numPoints, bool isNegative, bool isVertical, bool fixedUpperCorners, float xStart, float xEnd, float xExtent, float yExtent, float heightMin)
	{
		Vector3 pointOriented(float x, float h)
		{
			float height = isNegative ? -h : h;
			return isVertical ? new Vector3(height, x) : new Vector3(x, height);
		}

		Vector3[] points = new Vector3[numPoints];

		// set fixed corners
		points[0] = pointOriented(xStart, fixedUpperCorners ? yExtent : Random.Range(heightMin, yExtent));
		int corner1Idx = numPoints - 3;
		points[corner1Idx] = pointOriented(xEnd, fixedUpperCorners ? yExtent : Random.Range(heightMin, yExtent));
		points[numPoints - 2] = pointOriented(xEnd, -yExtent);
		points[numPoints - 1] = pointOriented(xStart, -yExtent);

		void assignMidpointsRecursive(int idxA, int idxB)
		{
			int idxMid = (idxA + idxB) / 2;
			if (idxMid == idxA || idxMid == idxB)
			{
				return;
			}

			// average & randomize
			float randomOffset = Random.Range(-1.0f, 1.0f) * Vector2.Distance(points[idxA], points[idxB]) / xExtent; // TODO: parameterize the amount of randomness
			Vector3 pointMid = (points[idxA] + points[idxB]) * 0.5f + pointOriented(0.0f, randomOffset);

			// clamp
			if (isVertical)
			{
				pointMid.x = isNegative ? Mathf.Min(-heightMin, pointMid.x) : Mathf.Max(heightMin, pointMid.x);
			}
			else
			{
				pointMid.y = isNegative ? Mathf.Min(-heightMin, pointMid.y) : Mathf.Max(heightMin, pointMid.y);
			}
			points[idxMid] = pointMid;

			// recurse
			assignMidpointsRecursive(idxA, idxMid);
			assignMidpointsRecursive(idxMid, idxB);
		}
		assignMidpointsRecursive(0, corner1Idx);

		return points;
	}

	private System.Tuple<Vector3, Vector3> TangentsFromSpline(Spline spline, Vector3 splinePos, int idx, int idxCount, bool groundCheck)
	{
		Vector3 pos = spline.GetPosition(idx);
		if (groundCheck && pos.y + splinePos.y <= 0.0f)
		{
			return System.Tuple.Create(Vector3.zero, Vector3.zero);
		}
		Vector3 leftDiff = spline.GetPosition((idx - 1).Modulo(idxCount)) - pos;
		Vector3 rightDiff = pos - spline.GetPosition((idx + 1).Modulo(idxCount));
		Vector3 diffAvgDir = (leftDiff.normalized + rightDiff.normalized).normalized;
		return System.Tuple.Create(leftDiff.magnitude * Random.Range(0.0f, 1.0f) * diffAvgDir, Random.Range(0.0f, 1.0f) * rightDiff.magnitude * -diffAvgDir);
	}

	private void OpenDoorway(DoorwayInfo doorwayInfo, bool open)
	{
		Debug.Assert(m_doorwayInfos.Contains(doorwayInfo));

		GameObject doorway = doorwayInfo.m_object;

		// enable/disable doorway
		if (!doorway.TryGetComponent(out PlatformEffector2D effector))
		{
			doorway.SetActive(!open);
		}
		else
		{
			// enable effector for dynamic collisions
			effector.enabled = open;
			doorway.GetComponent<Collider2D>().usedByEffector = open;

			// set layer for kinematic movement
			doorway.layer = (open ? GameController.Instance.m_layerOneWay : GameController.Instance.m_layerWalls).ToIndex();

			// change color/shadows for user visibility
			// TODO: match wall texture?
			SpriteRenderer renderer = doorway.GetComponent<SpriteRenderer>();
			renderer.color = open ? m_oneWayPlatformColor : m_wallColor;
			renderer.sprite = open ? m_floorPlatformSprite : (m_wallInfo.m_sprite != null ? m_wallInfo.m_sprite : renderer.sprite);
			doorway.GetComponent<ShadowCaster2D>().enabled = !open;
		}

		if (!open)
		{
			// move any newly colliding objects into room
			Vector2 doorwaySize = doorwayInfo.Size();
			Collider2D[] colliders = Physics2D.OverlapBoxAll(doorway.transform.position, doorwaySize, 0.0f);
			Vector2 intoRoom = -doorwayInfo.DirectionOutward();
			foreach (Collider2D collider in colliders)
			{
				if (collider.attachedRigidbody == null || collider.attachedRigidbody.bodyType == RigidbodyType2D.Static || collider.transform.parent != null)
				{
					continue; // ignore statics/children
				}
				collider.transform.position += (Vector3)(((Vector2)doorway.transform.position - (Vector2)collider.bounds.center) * intoRoom.Abs() + ((Vector2)collider.bounds.extents + doorwaySize * 0.5f) * intoRoom); // this essentially moves the object to the doorway center and then inward by the total extents of the two, all restricted to the doorway direction axis
			}
		}
	}

	private class AStarPath : System.IComparable<AStarPath>
	{
		public List<RoomController> m_pathRooms; // TODO: store DoorwayInfos to ensure correct paths w/ multiple connections between room pairs?
		public List<Vector2> m_pathPositions;
		public float m_distanceCur;
		public float m_distanceTotalEst;

		public int CompareTo(AStarPath other) => m_distanceTotalEst.CompareTo(other.m_distanceTotalEst);
	}

	private AStarPath RoomPath(GameObject start, GameObject end, PathFlags flags, float extentY = -1.0f, float upwardMax = float.MaxValue, float incrementDegrees = -1.0f)
	{
		Debug.Assert(Bounds.Contains(start.transform.position)); // TODO: explicitly handle startPos being slightly outside this room due to objects overlapping multiple rooms?

		// determine start/end points
		// TODO: base end position on penultimate position?
		bool nearestPoints = flags.BitsSet(PathFlags.NearestEndPoints);
		Bounds startBbox = ChildBounds(start, false);
		startBbox.Expand(new Vector3(-m_physicsCheckEpsilon, -m_physicsCheckEpsilon));
		Vector2 startPos = nearestPoints ? startBbox.ClosestPoint(end.transform.position) : startBbox.center;
		Bounds endBbox = ChildBounds(end, false);
		endBbox.Expand(new Vector3(-m_physicsCheckEpsilon, -m_physicsCheckEpsilon));
		Vector2 endPos = nearestPoints ? endBbox.ClosestPoint(start.transform.position) : endBbox.center;

		List<RoomController> visitedRooms = new();
		HeapQueue<AStarPath> paths = new();
		paths.Push(new() { m_pathRooms = new() { this }, m_pathPositions = new() { startPos }, m_distanceCur = 0.0f, m_distanceTotalEst = startPos.ManhattanDistance(endPos) }); // NOTE the use of Manhattan distance since diagonal traversal isn't currently used
		RoomController endRoom = FromPosition(endPos);

		AStarPath pathItr;
		while (paths.TryPop(out pathItr) && pathItr.m_pathRooms.Last() != endRoom)
		{
			RoomController roomCur = pathItr.m_pathRooms.Last();
			if (visitedRooms.Contains(roomCur))
			{
				continue;
			}
			visitedRooms.Add(roomCur);

			foreach (DoorwayInfo info in roomCur.m_doorwayInfos)
			{
				RoomController roomNext = info.ConnectedRoom;
				if (roomNext == null || info.IsObstructed(flags))
				{
					continue;
				}

				// NOTE that we don't immediately break if roomNext is endRoom, since there can be multiple valid paths and we're only guaranteed to have the shortest length when popping from paths[]

				// get doorway info
				Vector2 posPrev = pathItr.m_pathPositions.Last();
				Vector2 posNext = endPos; // TODO: determine actual next point somehow?
				bool ignoreGravity = flags.BitsSet(PathFlags.IgnoreGravity);
				Bounds bbox1 = ChildBounds(info.m_object);
				Bounds bbox2 = ChildBounds(info.m_infoReverse.m_object);
				float doorwayDirY = info.DirectionOutward().y;
				bool isHorizontalDoorway = doorwayDirY == 0.0f;

				// tweak bboxes to push each point into the room a little to prevent paths running inside walls
				float sizeY = 2.0f * extentY; // NOTE that Bounds.Expand() apparently expands each AXIS rather than each SIDE by the given amount, meaning that to move both the positive and negative directions by extentY requires using sizeY
				Vector3 expansion = isHorizontalDoorway ? new(sizeY, -sizeY) : new(-sizeY, sizeY);
				bbox1.Expand(expansion);
				bbox2.Expand(expansion);

				// determine where to pass between the rooms
				List<Vector2> connectionPoints = new() { posPrev, ignoreGravity ? bbox1.ClosestPoint(posPrev) : bbox1.center * 2 - bbox1.ClosestPoint(bbox2.center), ignoreGravity ? bbox2.ClosestPoint(posNext) : bbox2.center * 2 - bbox2.ClosestPoint(bbox1.center) }; // NOTE that we don't use Connection() since we want this particular doorway and have already done obstruction checking // NOTE that we push each point into the room a little to prevent paths running inside walls

				// edit y-coordinate at doorways to match character/object midpoint
				if (extentY >= 0.0f && !ignoreGravity)
				{
					float yAdjustment = isHorizontalDoorway ? bbox1.min.y : extentY; // TODO: don't assume all horizontal doors are at equal height to each other?
					for (int i = 1; i < connectionPoints.Count; ++i) // NOTE that we skip over posPrev
					{
						connectionPoints[i] = new(connectionPoints[i].x, isHorizontalDoorway ? yAdjustment : connectionPoints[i].y + yAdjustment * (i == 1 ? -doorwayDirY : doorwayDirY));
					}
				}

				// add the end point segment to be restricted if this is the last room
				if (roomNext == endRoom)
				{
					connectionPoints.Add(endPos);
				}

				// restrict segment angles
				if (incrementDegrees > 0.0f)
				{
					float incrementRadians = Mathf.Deg2Rad * incrementDegrees;
					for (int i = 1; i < connectionPoints.Count; ++i)
					{
						i += RestrictAngleTo(connectionPoints, i, incrementRadians);
					}
				}

				// measure to the end point if this is the last room; otherwise, to the last connection point
				Vector2 posNew = roomNext == endRoom ? endPos : connectionPoints.Last();
				float distanceCurNew = pathItr.m_distanceCur + posPrev.ManhattanDistance(posNew); // NOTE the use of Manhattan distance since diagonal traversal isn't currently used

				bool tooHigh = false;
				for (int i = 1; !tooHigh && i < connectionPoints.Count - 1; ++i) // NOTE that we don't check posNew.y against connectionPoints.Last().y, since posNew will either be the same as connectionPoints.Last() or endPos, and we ignore endPos since targets can generally move around w/i the end room // TODO: parameterize whether to ignore endPos?
				{
					tooHigh |= connectionPoints[i].y - connectionPoints[i - 1].y > upwardMax;
				}
				if (tooHigh)
				{
					continue;
				}

				// NOTE the copy to prevent editing other branches' paths
				// TODO: efficiency?
				List<RoomController> roomPathNew = new(pathItr.m_pathRooms) { roomNext };
				List<Vector2> posPathNew = new(pathItr.m_pathPositions);
				connectionPoints.RemoveAt(0); // since this is redundant w/ posPathNew.Last()
				posPathNew.AddRange(connectionPoints);

				// TODO: weight distances based on jumps/traversal required?
				paths.Push(new() { m_pathRooms = roomPathNew, m_pathPositions = posPathNew, m_distanceCur = distanceCurNew, m_distanceTotalEst = distanceCurNew + posNew.ManhattanDistance(endPos) });
			}
		}

		// ensure a complete path even if start/end are within the same room
		if (pathItr != null && pathItr.m_pathPositions.Count <= 1)
		{
			pathItr.m_pathPositions.Add(endPos);
		}

		return pathItr; // TODO: find path to closest reachable point if pathItr is null?
	}

	private static int RestrictAngleTo(List<Vector2> connectionPoints, int idx, float incrementRadians)
	{
		// measure unedited segment against desired increment
		Vector2 start = connectionPoints[idx - 1];
		Vector2 diff = connectionPoints[idx] - start;
		float diffRadians = Utility.ZRadians(diff);
		float numIncrements = diffRadians / incrementRadians;
		float incrementsFract = numIncrements.Modulo(1.0f);
		if (incrementsFract.FloatEqual(0.0f))
		{
			return 0;
		}

		// determine angles/side of relevant triangle
		float lowerInc = Mathf.Floor(numIncrements) * incrementRadians;
		float radiansAboveInc = diffRadians - lowerInc;
		float radiansBelowInc = lowerInc + incrementRadians - diffRadians;
		Debug.Assert(radiansAboveInc > 0.0f && radiansBelowInc > 0.0f && radiansAboveInc + radiansBelowInc < Mathf.PI);
		float angleC = Mathf.PI - radiansAboveInc - radiansBelowInc;

		// by Law of Sines: a / sin(A) == b / sin(B) --> diff.magnitude / sin(angleC) == dist / sin(numIncrements % 1.0f > 0.5f ? radiansAboveInc : radiansBelowInc)
		float dist = diff.magnitude / Mathf.Sin(angleC) * Mathf.Sin(incrementsFract > 0.5f ? radiansAboveInc : radiansBelowInc);
		float closestIncRadians = Mathf.Round(numIncrements) * incrementRadians;
		Vector2 intermediate = start + dist * new Vector2(Mathf.Cos(closestIncRadians), Mathf.Sin(closestIncRadians));

		connectionPoints.Insert(idx, intermediate);
		return 1;
	}

	private Vector2[] Connection(RoomController to, PathFlags flags, Vector2 priorityPos)
	{
		bool foundConnection = false;
		foreach (DoorwayInfo info in m_doorwayInfos.OrderBy(info => Vector2.Distance(priorityPos, info.m_object.transform.position)))
		{
			if (info.ConnectedRoom != to || info.m_infoReverse.ConnectedRoom != this)
			{
				continue;
			}
			foundConnection = true;
			if (info.IsObstructed(flags) || info.m_infoReverse.IsObstructed(flags, true/*?*/))
			{
				continue;
			}

			return new Vector2[] { info.m_object.transform.position, info.m_infoReverse.m_object.transform.position }; // TODO: edit y-coordinate of horizontal doorways to match character midpoint?
		}

		Debug.Assert(foundConnection);
		return null;
	}

	private RoomController FromPositionInternal(Vector3 pos3D)
	{
		// TODO: efficiency?

		if (Bounds.Contains(pos3D))
		{
			return this;
		}

		foreach (DoorwayInfo info in m_doorwayInfos)
		{
			if (info.ChildRoom == null)
			{
				continue;
			}
			RoomController childRoom = info.ChildRoom.FromPositionInternal(pos3D);
			if (childRoom != null)
			{
				return childRoom;
			}
		}
		return null;
	}
}
