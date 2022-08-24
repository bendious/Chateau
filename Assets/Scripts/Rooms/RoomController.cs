using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;


[DisallowMultipleComponent]
public class RoomController : MonoBehaviour
{
	[System.Serializable] private struct DirectionalDoors
	{
		public Vector2 m_direction;
		public WeightedObject<GameObject>[] m_prefabs;
	};


	[SerializeField] private WeightedObject<GameObject>[] m_npcPrefabs;

	[SerializeField] private WeightedObject<GameObject>[] m_gatePrefabs;
	[SerializeField] private DirectionalDoors[] m_doorDirectionalPrefabs;
	[SerializeField] private WeightedObject<GameObject>[] m_doorSecretPrefabs;
	[SerializeField] private WeightedObject<GameObject>[] m_cutbackPrefabs;
	[SerializeField] private GameObject[] m_lockOrderedPrefabs;

	[SerializeField] private WeightedObject<GameObject>[] m_exteriorPrefabsAbove;
	[SerializeField] private WeightedObject<GameObject>[] m_exteriorPrefabsBelow;

	[SerializeField] private Sprite m_floorPlatformSprite;
	[SerializeField] private GameObject m_doorSealVFX;

	[SerializeField] private WeightedObject<GameObject>[] m_spawnPointPrefabs;
	[SerializeField] private int m_spawnPointsMax = 4;

	[SerializeField] private GameObject m_backdrop;

	[SerializeField] private GameObject[] m_walls;

	[SerializeField] private WeightedObject<GameObject>[] m_ladderRungPrefabs;
	[SerializeField] private float m_ladderRungSkewMax = 0.2f;

	[SerializeField] private float m_cutbackBreakablePct = 0.5f;
	[SerializeField] private float m_furnitureLockPct = 0.5f;


	public static readonly Color m_oneWayPlatformColor = new(0.3f, 0.2f, 0.1f);


	public enum ObstructionCheck
	{
		None,
		Directional,
		LocksOnly,
		Full
	};


	public GameObject[] DoorwaysUpwardUnblocked => m_doorwayInfos.Where(info => !info.m_onewayBlocked && info.IsOpen() && info.DirectionOutward() == Vector2.up).Select(info => info.m_object).ToArray();

	public /*readonly*/ Bounds Bounds { get; private set; }

	public Bounds BoundsInterior { get {
		Bounds boundsInterior = Bounds; // NOTE the copy since Expand() modifies the given struct
		boundsInterior.Expand(new Vector3(-1.0f, -1.0f, float.MaxValue)); // TODO: dynamically determine wall thickness?
		return boundsInterior;
	} }

	public Vector2 ParentDoorwayPosition => Connection(m_doorwayInfos.Select(info => info.ParentRoom).First(parentRoom => parentRoom != null), ObstructionCheck.None, Vector2.zero)[1];

	public IEnumerable<RoomController> WithDescendants => new[] { this }.Concat(m_doorwayInfos.Where(info => info.ChildRoom != null).SelectMany(info => info.ChildRoom.WithDescendants)); // TODO: efficiency?

	public IEnumerable<Transform> BackdropsAboveGroundRecursive
	{ get {
		IEnumerable<Transform> childBackdrops = m_doorwayInfos.Where(info => info.ChildRoom != null).SelectMany(info => info.ChildRoom.BackdropsAboveGroundRecursive);
		return (Bounds.max.y > 0.0f) ? childBackdrops.Concat(new[] { m_backdrop.transform }) : childBackdrops;
	} }

	public RoomType RoomType { get; private set; }


	[System.Serializable]
	private class DoorwayInfo
	{
		public /*readonly*/ GameObject m_object;


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

		internal bool m_onewayBlocked;

		internal /*readonly*/ DoorwayInfo m_infoReverse;

		internal System.Lazy<bool> m_excludeSelf = new(() => Random.value < 0.5f, false); // TODO: more deliberate determination?


		private enum ConnectionType { None, Parent, Child, SiblingShallower, SiblingDeeper };
		private ConnectionType m_connectionType;


		internal Vector2 Size() => m_object.GetComponent<BoxCollider2D>().size * m_object.transform.localScale; // NOTE that we can't use Collider2D.bounds since this can be called before physics has run

		internal Vector2 DirectionOutward()
		{
			// TODO: don't assume convex room shapes?
			Vector2 roomToDoorway = (Vector2)m_object.transform.position - (Vector2)m_object.transform.parent.transform.position;
			Vector3 doorwaySize = Size();
			return doorwaySize.x > doorwaySize.y ? new(0.0f, Mathf.Sign(roomToDoorway.y)) : new(Mathf.Sign(roomToDoorway.x), 0.0f);
		}

		internal bool IsObstructed(ObstructionCheck checkLevel, bool ignoreOnewayBlockages = false)
		{
			RoomController room = m_object.transform.parent.GetComponent<RoomController>(); // TODO: cache reference?
			Debug.Assert(room != null && room.m_doorwayInfos.Contains(this));
			if (checkLevel == ObstructionCheck.None)
			{
				return false;
			}
			if (checkLevel == ObstructionCheck.Directional && room.m_layoutNodes.Any(fromNode => fromNode.m_children != null && fromNode.m_children.Count > 0 && fromNode.m_children.All(toNode => ConnectedRoom.m_layoutNodes.Contains(toNode))))
			{
				return false;
			}
			if (!ignoreOnewayBlockages && m_onewayBlocked) // TODO: check reverse one-way sometimes?
			{
				return true;
			}
			if (!IsOpen(true) || !m_infoReverse.IsOpen(true)) // NOTE that we check m_blocker separately from IsOpen(), since destructible blockers sometimes need to be ignored
			{
				return true;
			}
			if (m_blocker != null && m_blocker.GetComponents<Collider2D>().Any(collider => !collider.isTrigger && collider.isActiveAndEnabled) && (checkLevel == ObstructionCheck.Full || m_blocker.GetComponent<IUnlockable>() != null)) // NOTE that m_infoReverse.m_blocker should always be the same as m_blocker, so we only check one
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
	}
	[SerializeField]
	private /*readonly*/ DoorwayInfo[] m_doorwayInfos;

	private /*readonly*/ LayoutGenerator.Node[] m_layoutNodes;

	private /*readonly*/ GameObject[] m_spawnPoints;

	private /*readonly*/ RoomType.SpriteInfo m_wallInfo;
	private /*readonly*/ Color m_wallColor;


	private const float m_physicsCheckEpsilon = 0.1f; // NOTE that Utility.FloatEpsilon is too small to prevent false positives from rooms adjacent to the checked area


	public static T RandomWeightedByKeyCount<T>(IEnumerable<WeightedObject<T>> candidates, System.Func<T, int> candidateToKeyDiff, float scalarPerDiff = 0.5f)
	{
		// NOTE the copy to avoid altering existing weights
		WeightedObject<T>[] candidatesProcessed = candidates.Where(candidate => candidate.m_object != null).Select(candidate =>
		{
			int keyCountDiff = candidateToKeyDiff(candidate.m_object);
			return new WeightedObject<T> { m_object = candidate.m_object, m_weight = keyCountDiff < 0 ? 0.0f : candidate.m_weight / (1 + keyCountDiff * scalarPerDiff) };
		}).ToArray();
		return candidatesProcessed.Length <= 0 ? default : candidatesProcessed.RandomWeighted();
	}


	private void Awake()
	{
		Bounds = m_backdrop.GetComponent<SpriteRenderer>().bounds;
		ObjectDespawn.OnExecute += OnObjectDespawn;
	}

	private void Start()
	{
		if (transform.parent != null)
		{
			LinkRecursive();
		}
	}

	private void OnDestroy()
	{
		ObjectDespawn.OnExecute -= OnObjectDespawn;
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (m_layoutNodes == null || ConsoleCommands.LayoutDebugLevel == (int)ConsoleCommands.LayoutDebugLevels.None)
		{
			return;
		}

		Vector3 centerPosItr = Bounds.center;
		if (RoomType != null)
		{
			UnityEditor.Handles.Label(centerPosItr, RoomType.ToString()); // TODO: prevent drift from Scene camera?
		}

		foreach (LayoutGenerator.Node node in m_layoutNodes)
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
			if (info.m_onewayBlocked)
			{
				// draw simple arrow pointing inward to indicate allowed direction
				UnityEditor.Handles.DrawLines(new[] { info.m_object.transform.position + new Vector3(0.5f, 0.5f), info.m_object.transform.position, info.m_object.transform.position, 2.0f * info.m_infoReverse.m_object.transform.position - info.m_object.transform.position });
			}
		}
	}
#endif


	public void SetNodes(LayoutGenerator.Node[] layoutNodes)
	{
		Assert.IsNull(m_layoutNodes);
		m_layoutNodes = layoutNodes;
		foreach (LayoutGenerator.Node node in m_layoutNodes)
		{
			Debug.Assert(node.m_room == null);
			node.m_room = this;
		}
	}

	public void FinalizeRecursive(ref int doorwayDepth, ref int npcDepth)
	{
		// record/increment depths since we need the original values after passing the incremented values to our descendants
		int doorwayDepthLocal = doorwayDepth;
		int npcDepthLocal = npcDepth;
		npcDepth += m_layoutNodes.Count(node => node.m_type == LayoutGenerator.Node.Type.Npc);
		doorwayDepth += m_layoutNodes.Count(node => node.m_type == LayoutGenerator.Node.Type.Entrance || node.m_type == LayoutGenerator.Node.Type.ExitDoor);

		// spawn fixed-placement node architecture
		// NOTE the separate loops to ensure fixed-placement nodes are processed before flexible ones; also that this needs to be before flexibly-placed objects such as furniture
		float fillPct = 0.0f;
		Vector2 extentsInterior = BoundsInterior.extents;
		foreach (LayoutGenerator.Node node in m_layoutNodes)
		{
			switch (node.m_type)
			{
				case LayoutGenerator.Node.Type.Entrance:
					fillPct += SpawnDoor(doorwayDepthLocal, true, extentsInterior.x);
					++doorwayDepthLocal;
					break;
			}
		}

		// open cutbacks
		// NOTE that this has to be before flexible-placement spawning to avoid overlap w/ ladders
		if (GameController.Instance.m_allowCutbacks && m_layoutNodes.All(node => node.m_type != LayoutGenerator.Node.Type.RoomSecret && node.m_type != LayoutGenerator.Node.Type.RoomIndefinite))
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
				if (sibling == null || sibling.m_layoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomSecret || node.m_type == LayoutGenerator.Node.Type.RoomIndefinite)) // TODO: allow some cutbacks in indefinite room generation?
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
				int siblingDepthComparison = sibling.m_layoutNodes.Max(node => node.Depth).CompareTo(m_layoutNodes.Max(node => node.Depth));
				RoomController deepRoom = siblingDepthComparison < 0 ? this : sibling;
				RoomController shallowRoom = deepRoom == this ? sibling : this;
				RoomController lowRoom = direction.y.FloatEqual(0.0f) ? deepRoom : (direction.y > 0.0f ? this : sibling);
				bool noLadder = deepRoom != lowRoom;

				// determine traversability before adding cutback
				// TODO: use avatar max jump height once RoomPath() takes platforms into account?
				AStarPath pathLowToHigh = lowRoom.RoomPath(lowRoom.transform.position, (lowRoom == this ? sibling : this).transform.position, noLadder ? ObstructionCheck.Directional : ObstructionCheck.LocksOnly);
				AStarPath pathShallowToDeep = shallowRoom == lowRoom ? pathLowToHigh : shallowRoom.RoomPath(shallowRoom.transform.position, deepRoom.transform.position, noLadder ? ObstructionCheck.Directional : ObstructionCheck.LocksOnly);

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

				// maybe add one-way lock
				bool cutbackIsLocked = shallowRoom == lowRoom && siblingDepthComparison != 0 ? !noLadder : pathLowToHigh == null || pathShallowToDeep == null;
				Debug.Assert(noLadder || cutbackIsLocked || SceneManager.GetActiveScene().buildIndex == 0 || m_layoutNodes.First().AreaParents.Zip(sibling.m_layoutNodes.First().AreaParents, System.Tuple.Create).All(pair => pair.Item1 == pair.Item2), "Open cutback between separate areas?"); // TODO: don't assume 0th scene is open-concept?
				if (cutbackIsLocked || Random.value <= m_cutbackBreakablePct)
				{
					// add one-way lock
					int dummyLockIdx = -1;
					noLadder |= shallowRoom.SpawnGate(shallowRoom != this ? doorwayInfo : reverseInfo, cutbackIsLocked ? LayoutGenerator.Node.Type.Lock : LayoutGenerator.Node.Type.GateBreakable, doorwayInfo.m_excludeSelf.Value ? 0 : 1, ref dummyLockIdx, true); // NOTE the "reversed" DoorwayInfos to place the gate in deepRoom but as a child of shallowRoom, for better shadowing
				}
				if (pathLowToHigh != null && pathShallowToDeep != null) // NOTE that the two paths might be equivalent or going in opposite directions, so if either is null we know there's no loop, but if neither are, we still have to do more checks
				{
					// check for one-way loop traversal
					int pathIdx = 0;
					int posIdx = -1;
					DoorwayInfo[] pathDoorwaysForward = pathLowToHigh.m_pathRooms.GetRange(0, pathLowToHigh.m_pathRooms.Count - 1).Select(room =>
					{
						++pathIdx;
						posIdx += 2;
						return room.m_doorwayInfos.First(info => info.ConnectedRoom == pathLowToHigh.m_pathRooms[pathIdx] && (pathLowToHigh.m_pathPositions[posIdx] - (Vector2)info.m_object.transform.position).sqrMagnitude < 1.0f/*?*/); // TODO: simpler way of ensuring the correct doorway for room pairs w/ multiple connections?
					}).ToArray();
					bool canTraverseForward = !noLadder && pathDoorwaysForward.All(info => !info.m_infoReverse.m_onewayBlocked); // NOTE the forced reversed path direction when the cutback is one-way to avoid one-ways in opposite directions
					bool canTraverseBackward = pathDoorwaysForward.All(info => !info.m_onewayBlocked);

					if (canTraverseForward || canTraverseBackward)
					{
						// try creating one-ways
						int traversalDir = !canTraverseForward || (canTraverseBackward && Random.value < 0.5f) ? -1 : 1;
						for (int doorwayIdx = traversalDir > 0 ? 0 : pathDoorwaysForward.Length - 1; traversalDir < 0 ? doorwayIdx > 0 : doorwayIdx < pathDoorwaysForward.Length - 1; doorwayIdx += traversalDir)
						{
							DoorwayInfo infoForward = pathDoorwaysForward[doorwayIdx];
							DoorwayInfo info = traversalDir < 0 ? infoForward.m_infoReverse : infoForward;
							if (info.DirectionOutward() == Vector2.up) // TODO: non-vertical one-way blockages?
							{
								info.m_onewayBlocked = true;
							}
						}
					}
				}
				if (noLadder)
				{
					(direction.y > 0.0f ? doorwayInfo : reverseInfo).m_onewayBlocked = true;
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
		RoomType = (m_layoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomSecret) ? GameController.Instance.m_roomTypesSecret : GameController.Instance.m_roomTypes).Where(type =>
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
		}).Select(weightedObj => weightedObj.m_object).RandomWeighted(weightsScaled);

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
		IEnumerable<RoomController> areaParents = m_layoutNodes.SelectMany(node => node.AreaParents).Select(node => node.m_room).Distinct();
		RoomController areaParent = areaParents.FirstOrDefault(room => room.m_wallInfo != null);
		bool isAreaInit = areaParent == null;
		if (isAreaInit)
		{
			areaParent = areaParents.First();
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
				obj.GetComponent<SpriteRenderer>().color = m_wallColor; // TODO: slight variation?
			}
		}

		// finalize children
		// NOTE that this has to be BEFORE spawning ladders in order to ensure all cutbacks are opened first
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			if (doorwayInfo.ChildRoom != null)
			{
				doorwayInfo.ChildRoom.FinalizeRecursive(ref doorwayDepth, ref npcDepth);
			}
		}

		// spawn ladders
		if (m_ladderRungPrefabs != null && m_ladderRungPrefabs.Length > 0)
		{
			foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
			{
				if (doorwayInfo.m_onewayBlocked || doorwayInfo.ConnectedRoom == null || doorwayInfo.DirectionOutward().y <= 0.0f)
				{
					continue;
				}

				GameObject ladder = SpawnLadder(doorwayInfo.m_object);

				if (ladder != null)
				{
					fillPct += BoundsWithChildren(ladder).extents.x / extentsInterior.x;
				}
				else
				{
					doorwayInfo.m_onewayBlocked = true;
				}
			}
		}

		// spawn locks
		// NOTE that this is done before flexible node architecture since some locks (e.g. ladders) have required positioning
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			SpawnKeys(doorwayInfo, (unlockable, lockRoom, keyRooms) => unlockable.SpawnKeysStatic(lockRoom, keyRooms)); // NOTE that this has to be before furniture to ensure space w/o overlap
		}

		// spawn flexible node architecture
		foreach (LayoutGenerator.Node node in m_layoutNodes)
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
					RoomType.DecorationInfo info = RoomType.m_decorations[node.m_type - LayoutGenerator.Node.Type.TutorialPlatforms].m_object;
					GameObject prefab = info.m_prefab;
					Quaternion rotation = info.m_rotationDegreesMax == 0.0f ? Quaternion.identity : Quaternion.Euler(0.0f, 0.0f, Random.Range(-info.m_rotationDegreesMax, info.m_rotationDegreesMax));
					Instantiate(prefab, InteriorPosition(info.m_heightMin, info.m_heightMax, prefab, rotation), rotation, transform);
					if (node.m_type == LayoutGenerator.Node.Type.TutorialInteract && (GameController.Instance.m_avatars == null || !GameController.Instance.m_avatars.Any(avatar => avatar.GetComponentInChildren<ItemController>(true) != null)))
					{
						prefab = RoomType.m_itemPrefabs.RandomWeighted(); // TODO: spawn on table
						GameController.Instance.m_savableFactory.Instantiate(prefab, InteriorPosition(0.0f), Quaternion.identity);
					}
					break;

				case LayoutGenerator.Node.Type.ExitDoor:
					fillPct += SpawnDoor(doorwayDepthLocal, false, extentsInterior.x);
					++doorwayDepthLocal;
					break;

				case LayoutGenerator.Node.Type.Npc:
					int sceneIdx = SceneManager.GetActiveScene().buildIndex;
					if (sceneIdx == 0 ? npcDepthLocal > GameController.ZonesFinishedCount : npcDepthLocal + GameController.ZonesFinishedCount >= sceneIdx) // TODO: don't assume that the first scene is where NPCs congregate?
					{
						break;
					}
					GameObject npcPrefab = m_npcPrefabs.RandomWeighted();
					InteractDialogue npc = Instantiate(npcPrefab, InteriorPosition(0.0f) + (Vector3)npcPrefab.OriginToCenterY(), Quaternion.identity).GetComponent<InteractDialogue>();
					npc.Index = npcDepthLocal + sceneIdx;
					++npcDepthLocal;
					break;

				case LayoutGenerator.Node.Type.Enemy:
					// NOTE that this enemy won't be included in GameController.m_{waveEnemies/enemySpawnCounts}[] until room is opened and pathfinding succeeds
					GameObject enemyPrefab = GameController.Instance.m_enemyPrefabs.RandomWeighted(GameController.Instance.m_enemyPrefabs.Select(pair => 1.0f / pair.m_weight)).m_object; // NOTE that m_enemyPrefabs[] uses "weight" as enemy toughness rather than chance to spawn
					Instantiate(enemyPrefab, InteriorPosition(0.0f) + (Vector3)enemyPrefab.OriginToCenterY(), Quaternion.identity);
					break;

				default:
					break;
			}
		}

		// spawn furniture
		// NOTE that this has to be before keys to allow spawning them on furniture
		List<System.Tuple<FurnitureController, IUnlockable>> furnitureList = new();
		while (RoomType.m_furniturePrefabs.Length > 0 && fillPct < RoomType.m_fillPctMin)
		{
			FurnitureController furniture = Instantiate(RoomType.m_furniturePrefabs.RandomWeighted(), transform).GetComponent<FurnitureController>(); // NOTE that we have to spawn before placement due to potential size randomization
			Vector2 extentsEffective = extentsInterior * (1.0f - fillPct);
			float width = furniture.RandomizeSize(extentsEffective);
			furniture.transform.position = InteriorPosition(0.0f, furniture.gameObject, resizeAction: () => width = furniture.RandomizeSize(extentsEffective), failureAction: () =>
			{
				Simulation.Schedule<ObjectDespawn>().m_object = furniture.gameObject;
				furniture = null;
			});
			if (furniture == null)
			{
				break; // must have failed to find a valid placement position
			}
			IUnlockable furnitureLock = furniture.GetComponent<IUnlockable>();
			bool isLocked = furnitureLock != null && Random.value < m_furnitureLockPct; // TODO: more deliberate choice?
			furnitureList.Add(System.Tuple.Create(furniture, isLocked ? furnitureLock : null));
			fillPct += width * 0.5f / extentsInterior.x;

			if (furnitureLock != null)
			{
				if (isLocked)
				{
					furnitureLock.SpawnKeysStatic(this, new[] { this });
				}
				else
				{
					furnitureLock.Unlock(null, true);
				}
			}
		}

		// spawn items
		int itemCount = 0;
		int furnitureRemaining = furnitureList.Count - 1;
		foreach (System.Tuple<FurnitureController, IUnlockable> furniture in furnitureList)
		{
			itemCount += furniture.Item1.SpawnItems(m_layoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.BonusItems), RoomType, itemCount, furnitureRemaining);
			--furnitureRemaining;

			if (furniture.Item2 != null)
			{
				furniture.Item2.SpawnKeysDynamic(this, new[] { this }); // TODO: spaced-out keys?
			}
		}

		// spawn keys
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			SpawnKeys(doorwayInfo, (unlockable, lockRoom, keyRooms) => unlockable.SpawnKeysDynamic(lockRoom, keyRooms)); // NOTE that this has to be after furniture for item key placement
		}

		// spawn enemy spawn points
		m_spawnPoints = new GameObject[Random.Range(1, m_spawnPointsMax + 1)]; // TODO: base on room size?
		for (int spawnIdx = 0; spawnIdx < m_spawnPoints.Length; ++spawnIdx)
		{
			GameObject spawnPrefab = m_spawnPointPrefabs.RandomWeighted();
			Quaternion rotation = Quaternion.Euler(0.0f, 0.0f, Random.Range(0.0f, 360.0f));
			Vector3 spawnPos = InteriorPosition(float.MaxValue, spawnPrefab, rotation); // NOTE that we don't account for maximum enemy size, relying upon KinematicObject's checks to prevent getting stuck in walls
			m_spawnPoints[spawnIdx] = Instantiate(spawnPrefab, spawnPos, rotation, transform);
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
			float height = decorationTypeHeights[decoIdx];
			if (height == float.MinValue)
			{
				height = Random.Range(decoInfo.m_heightMin, Mathf.Min(roomHeight, decoInfo.m_heightMax));
				decorationTypeHeights[decoIdx] = height;
			}
			GameObject decoPrefab = decoInfo.m_prefab;
			Quaternion rotation = decoInfo.m_rotationDegreesMax == 0.0f ? Quaternion.identity : Quaternion.Euler(0.0f, 0.0f, Random.Range(-decoInfo.m_rotationDegreesMax, decoInfo.m_rotationDegreesMax));
			Vector3 spawnPos = InteriorPosition(height, height, decoPrefab, rotation, failureAction: () => decoPrefab = null);
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
		}
	}

	public void FinalizeTopDown(float widthIncrement)
	{
		// shared logic
		float widthIncrementHalf = widthIncrement * 0.5f;
		int layerMask = GameController.Instance.m_layerWalls | GameController.Instance.m_layerExterior;
		void trySpawningExteriorDecoration(WeightedObject<GameObject>[] prefabs, Vector3 position, float heightOverride, bool isBelow)
		{
			bool hasSpace = position.y > 0.0f && !Physics2D.OverlapArea(position + new Vector3(-widthIncrementHalf + m_physicsCheckEpsilon, isBelow ? -m_physicsCheckEpsilon : m_physicsCheckEpsilon), position + new Vector3(widthIncrementHalf - m_physicsCheckEpsilon, isBelow ? -1.0f : 1.0f), layerMask); // TODO: more nuanced height check?
			if (!hasSpace)
			{
				return;
			}

			SpriteRenderer renderer = Instantiate(prefabs.RandomWeighted(), position, transform.rotation, transform).GetComponent<SpriteRenderer>();
			renderer.size = new(widthIncrement, heightOverride >= 0.0f ? heightOverride : renderer.size.y);
			BoxCollider2D collider = renderer.GetComponent<BoxCollider2D>();
			collider.size = renderer.size;
			collider.offset = new(collider.offset.x, collider.size.y * (isBelow ? -0.5f : 0.5f));
		}

		// try to spawn exterior decorations
		// TODO: more deliberate choices? combine adjacent instantiations when possible?
		for (int i = 0, n = Mathf.RoundToInt(Bounds.size.x / widthIncrement); i < n; ++i)
		{
			// above
			Vector3 exteriorPos = new(Bounds.min.x + widthIncrementHalf + widthIncrement * i, Bounds.max.y, Bounds.center.z);
			trySpawningExteriorDecoration(m_exteriorPrefabsAbove, exteriorPos, -1.0f, false);

			// below
			exteriorPos.y = Bounds.min.y;
			RaycastHit2D raycast = Physics2D.Raycast(exteriorPos + Vector3.down * m_physicsCheckEpsilon, Vector2.down, transform.position.y, layerMask);
			trySpawningExteriorDecoration(m_exteriorPrefabsBelow, exteriorPos, raycast.distance == 0.0f ? transform.position.y : raycast.distance + m_physicsCheckEpsilon, true);
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

	public Vector3 InteriorPosition(float heightMax, GameObject preventOverlapPrefab = null, Quaternion? rotation = null, System.Action resizeAction = null, System.Action failureAction = null)
	{
		return InteriorPosition(0.0f, heightMax, preventOverlapPrefab, rotation, resizeAction, failureAction);
	}

	public Vector3 InteriorPosition(float heightMin, float heightMax, GameObject preventOverlapPrefab = null, Quaternion? rotation = null, System.Action resizeAction = null, System.Action failureAction = null)
	{
		Bounds boundsInterior = BoundsInterior;

		Vector3 pos;
		const int attemptsMax = 100;
		int failsafe = attemptsMax;
		do // TODO: efficiency?
		{
			// calculate overlap bbox
			Bounds bboxNew = new();
			if (preventOverlapPrefab != null)
			{
				bboxNew = BoundsWithChildren(preventOverlapPrefab, rotation);
				bboxNew.Expand(new Vector3(-Utility.FloatEpsilon, -Utility.FloatEpsilon, float.MaxValue)); // NOTE the slight x/y contraction to avoid always collecting the floor when up against it
			}

			float xDiffMax = boundsInterior.extents.x - bboxNew.extents.x;
			Debug.Assert(xDiffMax >= 0.0f);
			float yMaxFinal = Mathf.Min(heightMax, boundsInterior.size.y - bboxNew.size.y); // TODO: also count furniture surfaces as "floor"

			pos = new(boundsInterior.center.x + Random.Range(-xDiffMax, xDiffMax), transform.position.y + Random.Range(heightMin, yMaxFinal), transform.position.z); // NOTE the assumptions that the object position is on the floor of the room but not necessarily centered
			if (preventOverlapPrefab == null)
			{
				return pos;
			}

			// get points until no overlap
			// TODO: more deliberate iteration? avoid tendency to line up in a row?
			Vector3 centerOrig = bboxNew.center; // NOTE that we can't assume the bbox is centered
			float xSizeEffective = boundsInterior.size.x - bboxNew.size.x;
			int moveCount = attemptsMax;
			do
			{
				bboxNew.center = centerOrig + pos;

				bool overlap = false;
				foreach (Renderer renderer in GetComponentsInChildren<Renderer>().Where(r => r is SpriteRenderer or SpriteMask or MeshRenderer)) // NOTE that we would just exclude {Trail/VFX}Renderers except that VFXRenderer is inaccessible...
				{
					if (renderer.gameObject == m_backdrop || renderer.gameObject.layer == GameController.Instance.m_layerExterior.ToIndex())
					{
						continue;
					}
					if (renderer.bounds.Intersects(bboxNew))
					{
						overlap = true;
						break;
					}
					RectTransform tf = renderer.GetComponent<RectTransform>();
					if (tf != null && new Bounds((Vector3)tf.rect.center + tf.position, tf.rect.size).Intersects(bboxNew))
					{
						overlap = true;
						break;
					}
				}
				if (!overlap)
				{
					return pos;
				}

				pos.x += xSizeEffective / attemptsMax;
				pos.y = transform.position.y + Random.Range(heightMin, yMaxFinal);
				if (pos.x > boundsInterior.max.x - bboxNew.extents.x)
				{
					pos.x -= xSizeEffective;
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
			Debug.LogWarning("Failed to prevent room position overlap: " + preventOverlapPrefab.name + " at " + pos);
		}

		return pos;
	}

	public GameObject SpawnKey(GameObject prefab, float nonitemHeightMax, bool noLock)
	{
		bool isItem = prefab.GetComponent<Rigidbody2D>() != null; // TODO: ignore non-dynamic bodies?
		Vector3 spawnPos = isItem ? Vector3.zero : InteriorPosition(nonitemHeightMax, prefab); // TODO: prioritize placing non-items close to self if multiple in this room?
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
				spawnPos = InteriorPosition(0.0f);
			}
			else
			{
				FurnitureController chosenFurniture = validFurniture.ElementAt(Random.Range(0, validFurniture.Count())); // TODO: prioritize based on furniture type / existing items?
				return chosenFurniture.SpawnKey(prefab);
			}

			spawnPos += (Vector3)prefab.OriginToCenterY();
		}
		return prefab.GetComponent<ISavable>() == null ? Instantiate(prefab, spawnPos, Quaternion.identity) : GameController.Instance.m_savableFactory.Instantiate(prefab, spawnPos, Quaternion.identity);
	}

	public Vector3 SpawnPointRandom()
	{
		return m_spawnPoints[Random.Range(0, m_spawnPoints.Length)].transform.position;
	}

	public List<Vector2> PositionPath(Vector2 startPosition, Vector2 endPositionPreoffset, ObstructionCheck obstructionChecking = ObstructionCheck.None, float extentY = 0.0f, float upwardMax = float.MaxValue, Vector2 offsetMag = default, float incrementDegrees = -1.0f)
	{
		AStarPath roomPath = RoomPath(startPosition, endPositionPreoffset, obstructionChecking, extentY, upwardMax, incrementDegrees);
		if (roomPath == null)
		{
			// TODO: find path to closest reachable point instead?
			return null;
		}
		List<Vector2> waypointPath = roomPath.m_pathPositions;

		if (waypointPath.Count <= 1)
		{
			waypointPath.Add(endPositionPreoffset);
		}

		// offset end point
		// TODO: allow offset to cross room edges?
		float semifinalX = waypointPath[^2].x;
		Vector2 endPos = endPositionPreoffset + (semifinalX >= endPositionPreoffset.x ? offsetMag : new(-offsetMag.x, offsetMag.y));
		Bounds endRoomBounds = roomPath.m_pathRooms.Last().Bounds;
		endRoomBounds.Expand(new Vector3(-1.0f, -1.0f)); // TODO: dynamically determine wall thickness?
		waypointPath[^1] = endRoomBounds.Contains(new(endPos.x, endPos.y, endRoomBounds.center.z)) ? endPos : endRoomBounds.ClosestPoint(endPos); // TODO: flip offset if closest interior point is significantly different from endPos?

		if (incrementDegrees > 0.0f)
		{
			RestrictAngleTo(waypointPath, waypointPath.Count - 1, incrementDegrees * Mathf.Deg2Rad);
		}

		return waypointPath;
	}

	public RoomController SpawnChildRoom(GameObject roomPrefab, LayoutGenerator.Node[] layoutNodes, Vector2[] allowedDirections, ref int orderedLockIdx)
	{
		// prevent putting keys behind their lock
		// NOTE that we check all nodes' depth even though all nodes w/i a single room should be at the same depth
		if (layoutNodes.Max(node => node.Depth) < m_layoutNodes.Min(node => node.Depth))
		{
			return null;
		}

		// ensure areas end up grouped under a single room rather than spread out in different directions
		bool isSameArea = m_layoutNodes.First().AreaParents == layoutNodes.First().AreaParents; // NOTE the assumption that all nodes w/i a single room share an area
		RoomController areaHeadRoom = isSameArea ? null : m_doorwayInfos.Select(info => info.ChildRoom).FirstOrDefault(childRoom => childRoom != null && childRoom.m_layoutNodes.First().AreaParents == layoutNodes.First().AreaParents);
		if (areaHeadRoom != null)
		{
			return areaHeadRoom.SpawnChildRoom(roomPrefab, layoutNodes, allowedDirections, ref orderedLockIdx);
		}

		int orderedLockIdxTmp = orderedLockIdx; // due to not being able to use an outside reference inside a lambda
		RoomController childRoom = DoorwaysRandomOrder(i =>
		{
			DoorwayInfo doorway = m_doorwayInfos[i];
			if (doorway.ConnectedRoom != null)
			{
				return null;
			}

			// maybe replace/remove
			MaybeReplaceDoor(i, roomPrefab, layoutNodes, allowedDirections, ref orderedLockIdxTmp);
			return doorway.ChildRoom;
		});
		orderedLockIdx = orderedLockIdxTmp;

		if (childRoom != null)
		{
			return childRoom;
		}

		// NOTE that if we ever return to some rooms requiring direct parent-child connection, they should early-out here

		// try spawning from children
		RoomController newRoom = DoorwaysRandomOrder(i =>
		{
			DoorwayInfo doorway = m_doorwayInfos[i];
			if (doorway.ChildRoom == null)
			{
				return null;
			}
			return doorway.ChildRoom.SpawnChildRoom(roomPrefab, layoutNodes, allowedDirections, ref orderedLockIdxTmp);
		});
		orderedLockIdx = orderedLockIdxTmp;
		return newRoom;
	}

	public GameObject SpawnLadder(GameObject doorway, GameObject prefabForced = null, bool spawnBunched = false)
	{
		Assert.IsTrue(m_doorwayInfos.Any(info => info.m_object == doorway || info.m_blocker == doorway));

		// determine rung count/height
		GameObject ladderRungPrefab = prefabForced != null ? prefabForced : m_ladderRungPrefabs.RandomWeighted();
		DistanceJoint2D firstJoint = ladderRungPrefab.GetComponent<DistanceJoint2D>(); // TODO: genericize?
		bool hanging = firstJoint != null;
		float yTop = doorway.transform.position.y - (hanging ? 0.0f : 1.5f); // TODO: base top distance on character height
		float heightDiff = yTop - transform.position.y; // TODO: don't assume pivot point is always the place to stop?
		float rungOnlyHeight = ladderRungPrefab.GetComponent<SpriteRenderer>().size.y;
		float rungHeightTotal = hanging ? firstJoint.distance : rungOnlyHeight;
		int rungCount = Mathf.RoundToInt(heightDiff / rungHeightTotal) - (hanging ? 1 : 0);
		if (!hanging)
		{
			rungHeightTotal = heightDiff / rungCount;
		}

		Vector3 posItr = doorway.transform.position;
		GameObject firstRung = null;
		posItr.y = yTop - (hanging ? 0.0f : rungHeightTotal);
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
					joint.connectedAnchor = (Vector2)joint.transform.position + new Vector2(joint.anchor.x, 0.0f);
				}
			}
			bodyPrev = ladder.GetComponent<Rigidbody2D>();

			if (!hanging)
			{
				// resize
				// NOTE that we have to adjust bottom-up ladders to ensure the top rung is within reach of any combination lock above it
				SpriteRenderer renderer = ladder.GetComponent<SpriteRenderer>();
				renderer.size = new(renderer.size.x, rungHeightTotal);
				BoxCollider2D collider = ladder.GetComponent<BoxCollider2D>();
				collider.size = new(collider.size.x, rungHeightTotal);
				collider.offset = new(collider.offset.x, rungHeightTotal * 0.5f);
			}

			// iterate
			posItr.x += Random.Range(-m_ladderRungSkewMax, m_ladderRungSkewMax); // TODO: guarantee AI navigability? clamp to room size?
			posItr.y -= spawnBunched ? rungOnlyHeight : rungHeightTotal;
		}

		m_doorwayInfos.First(info => info.m_object == doorway || info.m_blocker == doorway).m_onewayBlocked = false;
		return firstRung;
	}

	public void SealRoom(bool seal)
	{
		for (int i = 0; i < m_doorwayInfos.Length; ++i)
		{
			DoorwayInfo doorwayInfo = m_doorwayInfos[i];
			if (doorwayInfo.ConnectedRoom == null)
			{
				continue;
			}

			bool wasOpen = doorwayInfo.IsOpen();
			OpenDoorway(doorwayInfo, !seal);

			if (seal && wasOpen)
			{
				GameObject doorway = doorwayInfo.m_object;
				Vector2 doorwaySize = doorwayInfo.Size();
				VisualEffect vfx = Instantiate(m_doorSealVFX, doorway.transform.position + new Vector3(0.0f, -0.5f * doorwaySize.y), Quaternion.identity).GetComponent<VisualEffect>();
				vfx.SetVector3("StartAreaSize", new(doorwaySize.x, 0.0f));

				doorway.GetComponent<AudioSource>().Play();
				// TODO: animation?
			}
		}

		GameController.Instance.GetComponent<CompositeShadowCaster2D>().enabled = true; // NOTE that the top-level caster has to start disabled due to an assert from CompositeShadowCaster2D when empty of child casters
		GetComponent<CompositeShadowCaster2D>().enabled = seal;
		transform.SetParent(seal ? null : GameController.Instance.transform);
	}

	// called via SendMessage(RoomType.m_preconditionName)
	public void IsAboveGround(SendMessageValue<float> result)
	{
		result.m_out = transform.position.y >= 0.0f ? 1.0f : 0.0f; // NOTE that the ground floor is always at y=0
	}

	// called via SendMessage(RoomType.m_preconditionName)
	public void IsBelowGround(SendMessageValue<float> result)
	{
		result.m_out = transform.position.y < 0.0f ? 1.0f : 0.0f; // NOTE that the ground floor is always at y=0
	}

	// called via SendMessage(RoomType.m_preconditionName)
	public void PreferDeadEnds(SendMessageValue<float> result)
	{
		result.m_out = m_doorwayInfos.Any(info => info.ChildRoom != null || info.SiblingShallowerRoom != null || info.SiblingDeeperRoom != null) ? 1.0f / GameController.Instance.m_roomTypes.Length : GameController.Instance.m_roomTypes.Length; // TODO: variable preference factor?
	}


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

	private T DoorwaysRandomOrder<T>(System.Func<int, T> f)
	{
		int[] order = Enumerable.Range(0, m_doorwayInfos.Length).OrderBy(i => Random.value).ToArray();
		foreach (int i in order)
		{
			T result = f(i);
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
			doorwayInfo.m_onewayBlocked = false;
			if (doorwayInfo.IsOpen(true))
			{
				LinkRecursive();
			}
		}
	}

	private void LinkRecursive()
	{
		// group this room's shadow casters under the top-level GameController caster
		GameController.Instance.GetComponent<CompositeShadowCaster2D>().enabled = true; // NOTE that the top-level caster has to start disabled due to an assert from CompositeShadowCaster2D when empty of child casters
		GetComponent<CompositeShadowCaster2D>().enabled = false;
		transform.SetParent(GameController.Instance.transform);

		// runtime generation if requested
		const int depthMax = 20; // TODO: parameterize? delete existing rooms rather than limiting?
		if (!m_doorwayInfos.Any(info => info.ChildRoom != null) && m_layoutNodes.Any(node => node.m_type == LayoutGenerator.Node.Type.RoomIndefinite && node.Depth < depthMax))
		{
			int dummyIdx = -1;
			List<LayoutGenerator.Node> newNodes = null;
			for (int i = 0, n = Random.Range(1, 3); i < n; ++i)
			{
				// create and link nodes
				if (newNodes == null)
				{
					newNodes = new() { new(LayoutGenerator.Node.Type.Secret, new() { new(LayoutGenerator.Node.Type.TightCoupling, new() { new(LayoutGenerator.Node.Type.RoomIndefinite) }) }) }; // TODO: streamline?
					foreach (LayoutGenerator.Node node in m_layoutNodes)
					{
						node.AddChildren(newNodes);
					}
				}

				// create room
				RoomController newRoom = SpawnChildRoom(GameController.Instance.m_roomPrefabs.RandomWeighted(), newNodes.SelectMany(node => node.WithDescendants).ToArray(), new[] { Vector2.left, Vector2.right, Vector2.down }, ref dummyIdx); // TODO: allow upward generation as long as it doesn't break through the ground?
				if (newRoom != null)
				{
					newRoom.FinalizeRecursive(ref dummyIdx, ref dummyIdx);
					newNodes = null;
				}
			}

			// cleanup if the last room was canceled
			if (newNodes != null)
			{
				foreach (LayoutGenerator.Node node in newNodes)
				{
					node.DirectParents.Remove(node);
				}
			}
		}

		// recurse into visible children
		foreach (DoorwayInfo info in m_doorwayInfos)
		{
			RoomController childRoom = info.ChildRoom;
			static bool allowsLight(DoorwayInfo info) => info.m_blocker == null || info.m_blocker.GetComponent<ShadowCaster2D>() == null;
			if (childRoom != null && allowsLight(info) && allowsLight(info.m_infoReverse))
			{
				childRoom.LinkRecursive();
			}
		}
	}

	private static Bounds BoundsWithChildren(GameObject obj, Quaternion? rotation = null)
	{
		Renderer[] renderers = obj.GetComponentsInChildren<Renderer>().Where(r => r is SpriteRenderer or SpriteMask or MeshRenderer).ToArray(); // NOTE that we would just exclude {Trail/VFX}Renderers except that VFXRenderer is inaccessible...
		Bounds SemiLocalBounds(Renderer r)
		{
			Bounds b = r.localBounds; // TODO: handle object rotation?
			b.center += r.transform.position - obj.transform.position;
			return b;
		}
		Bounds bboxNew = SemiLocalBounds(renderers.First()); // NOTE that we can't assume the local origin should always be included
		foreach (Renderer renderer in renderers)
		{
			bboxNew.Encapsulate(SemiLocalBounds(renderer));
		}
		RectTransform[] tfs = obj.GetComponentsInChildren<RectTransform>();
		foreach (RectTransform tf in tfs)
		{
			bboxNew.Encapsulate(new Bounds(tf.rect.center, tf.rect.size));
		}

		Quaternion rotationFinal = rotation != null ? rotation.Value : obj.transform.rotation;
		if (rotationFinal == Quaternion.identity)
		{
			return bboxNew;
		}

		// handle rotation by reseting and expanding to rotated corners
		// TODO: efficiency? don't assume rotation around center?
		Vector3 centerOrig = bboxNew.center;
		Vector3 extentsOrig = bboxNew.extents;
		bboxNew.extents = Vector3.zero;
		for (int i = -1; i < 2; i += 2)
		{
			for (int j = -1; j < 2; j += 2)
			{
				Vector2 corner = centerOrig + rotationFinal * Vector3.Scale(extentsOrig, new(i, j, 1.0f));
				bboxNew.Encapsulate(corner);
			}
		}

		return bboxNew;
	}

	private LayoutGenerator.Node GateNodeToChild(LayoutGenerator.Node[] childNodes, params LayoutGenerator.Node.Type[] gateTypes)
	{
		IEnumerable<LayoutGenerator.Node> ancestors = m_layoutNodes.Select(node => node.FirstCommonAncestor(childNodes)).Distinct();
		return ancestors.FirstOrDefault(node => gateTypes.Contains(node.m_type) && childNodes.Any(childNode => childNode.DirectParents.Contains(node))); // TODO: ensure gates are placed even if a room ends up between the gate and child rooms?
	}

	// TODO: inline?
	private void MaybeReplaceDoor(int index, GameObject roomPrefab, LayoutGenerator.Node[] childNodes, Vector2[] allowedDirections, ref int orderedLockIdx)
	{
		DoorwayInfo doorwayInfo = m_doorwayInfos[index];
		Vector2 replaceDirection = doorwayInfo.DirectionOutward();
		if (allowedDirections != null && !allowedDirections.Contains(replaceDirection))
		{
			return;
		}

		GameObject doorway = doorwayInfo.m_object;
		Assert.IsNull(doorwayInfo.ConnectedRoom);
		Assert.AreApproximatelyEqual(replaceDirection.magnitude, 1.0f);

		// determine child position
		RoomController otherRoom = roomPrefab.GetComponent<RoomController>();
		Bounds childBounds = otherRoom.m_backdrop.GetComponent<SpriteRenderer>().bounds; // NOTE that we can't use m_bounds since uninstantiated prefabs don't have Awake() called on them
		Vector2 doorwayPos = doorway.transform.position;
		int reverseIdx = -1;
		float doorwayToEdge = Mathf.Min(Bounds.max.x - doorwayPos.x, Bounds.max.y - doorwayPos.y, doorwayPos.x - Bounds.min.x, doorwayPos.y - Bounds.min.y); // TODO: don't assume convex rooms?
		Vector2 childPivotPos = Vector2.zero;
		Vector2 childPivotToCenter = childBounds.center - roomPrefab.transform.position;
		bool isOpen = false;
		foreach (int idxCandidate in otherRoom.DoorwayReverseIndices(replaceDirection).OrderBy(i => Random.value))
		{
			Vector2 childDoorwayPosLocal = otherRoom.m_doorwayInfos[idxCandidate].m_object.transform.position;
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

		RoomController childRoom = Instantiate(roomPrefab, childPivotPos, Quaternion.identity).GetComponent<RoomController>();
		doorwayInfo.ChildRoom = childRoom;
		DoorwayInfo reverseDoorwayInfo = childRoom.m_doorwayInfos[reverseIdx];
		doorwayInfo.m_infoReverse = reverseDoorwayInfo;
		reverseDoorwayInfo.ParentRoom = this;
		reverseDoorwayInfo.m_infoReverse = doorwayInfo;

		LayoutGenerator.Node blockerNode = GateNodeToChild(childNodes, LayoutGenerator.Node.Type.Lock, LayoutGenerator.Node.Type.LockOrdered, LayoutGenerator.Node.Type.GateBreakable, LayoutGenerator.Node.Type.Secret);
		if (blockerNode != null)
		{
			doorwayInfo.m_onewayBlocked |= SpawnGate(doorwayInfo, blockerNode.m_type, blockerNode.DirectParents.Count(node => node.m_type == LayoutGenerator.Node.Type.Key && (!doorwayInfo.m_excludeSelf.Value || node.m_room != this)), ref orderedLockIdx, false);
		}

		childRoom.SetNodes(childNodes);

		OpenDoorway(doorwayInfo, true);
		childRoom.OpenDoorway(reverseDoorwayInfo, true);
	}

	private float SpawnDoor(int doorwayDepth, bool isEntrance, float extentsInteriorX)
	{
		GameObject[] doorInteractPrefabs = GameController.Instance.m_doorInteractPrefabs;
		GameObject doorPrefab = doorInteractPrefabs[System.Math.Min(doorInteractPrefabs.Length - 1, doorwayDepth)];
		InteractScene doorInteract = Instantiate(doorPrefab, isEntrance ? transform.position : InteriorPosition(0.0f, 0.0f, doorPrefab), Quaternion.identity, transform).GetComponent<InteractScene>();
		if (doorInteract != null)
		{
			doorInteract.Depth = doorwayDepth;
		}
		return BoundsWithChildren(doorInteract.gameObject).extents.x / extentsInteriorX;
	}

	private bool SpawnGate(DoorwayInfo doorwayInfo, LayoutGenerator.Node.Type type, int preferredKeyCount, ref int orderedLockIdx, bool isCutback)
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
		WeightedObject<GameObject>[] directionalGates = m_doorDirectionalPrefabs.FirstOrDefault(pair => pair.m_direction == replaceDirection).m_prefabs; // NOTE that even if we don't use this list to choose, we still check it for ladder suppression
		WeightedObject<GameObject>[] nondirectionalGates = isOrderedOrSecret ? null : isCutback ? m_cutbackPrefabs : m_gatePrefabs;
		IEnumerable<WeightedObject<GameObject>> gatesFinal = isOrdered ? null : isSecret ? m_doorSecretPrefabs : directionalGates != null ? directionalGates.Concat(nondirectionalGates) : nondirectionalGates;
		gatesFinal = isOrdered ? null : gatesFinal.Where(weightedObj => isLock ? weightedObj.m_object.GetComponentInChildren<IUnlockable>() != null : weightedObj.m_object.GetComponentInChildren<IUnlockable>() == null).CombineWeighted(isCutback ? GameController.Instance.m_cutbackPrefabs : GameController.Instance.m_gatePrefabs);

		// pick & create gate
		GameObject blockerPrefab = isOrdered ? m_lockOrderedPrefabs[System.Math.Min(orderedLockIdx++, m_lockOrderedPrefabs.Length - 1)] : RandomWeightedByKeyCount(gatesFinal, prefab =>
		{
			LockController lockComp = prefab.GetComponent<LockController>();
			IEnumerable<WeightedObject<LockController.KeyInfo>> keys = lockComp == null ? null : lockComp.m_keyPrefabs;
			keys = keys?.CombineWeighted(GameController.Instance.m_keyPrefabs, info => info.m_object.m_prefabs.Select(info => info.m_object).FirstOrDefault(prefab => GameController.Instance.m_keyPrefabs.Any(key => key.m_object == prefab)), pair => pair.m_object);
			keys = keys?.Where(key => key.m_object.m_keyCountMax >= preferredKeyCount);
			return keys == null || keys.Count() <= 0 ? -preferredKeyCount : keys.Min(key => key.m_object.m_keyCountMax - preferredKeyCount);
		});
		Debug.Assert(doorwayInfo.m_blocker == null);
		doorwayInfo.m_blocker = Instantiate(blockerPrefab, doorwayInfo.m_object.transform.position, Quaternion.identity, transform);

		// set references
		IUnlockable unlockable = doorwayInfo.m_blocker.GetComponentInChildren<IUnlockable>();
		if (unlockable != null)
		{
			unlockable.Parent = gameObject;
		}
		reverseInfo.m_blocker = doorwayInfo.m_blocker;
		if (isCutback && isLock)
		{
			reverseInfo.m_onewayBlocked = true;
		}

		// resize/recolor gate to fit doorway
		SpriteRenderer[] renderers = doorwayInfo.m_blocker.GetComponentsInChildren<SpriteRenderer>();
		Vector2 size = doorwayInfo.Size();
		int i = 0;
		foreach (SpriteRenderer renderer in renderers)
		{
			renderer.size = size;
			renderer.GetComponent<BoxCollider2D>().size = size;
			if (isSecret)
			{
				renderer.color = m_wallColor; // NOTE that m_wallColor generally isn't set yet, but FinalizeRecursive() will also iterate over existing doorway blockers when m_wallColor is set
			}

			// TODO: don't assume that multi-part gates are synonymous w/ one-way breakable gates?
			if (i > 0)
			{
				renderer.transform.localPosition = replaceDirection * size * i;
			}
			if (renderers.Length > 1)
			{
				Health health = renderer.GetComponent<Health>();
				if (health != null)
				{
					health.m_invincibilityDirection = replaceDirection;
				}
			}

			VisualEffect vfx = renderer.GetComponent<VisualEffect>();
			if (vfx != null)
			{
				vfx.SetVector3("StartAreaSize", size);
			}

			// update shadow caster shape
			ShadowCaster2D shadowCaster = renderer.GetComponent<ShadowCaster2D>();
			if (shadowCaster != null)
			{
				Vector3 extents = size * 0.5f;
				Vector3[] shapePath = new Vector3[] { new(-extents.x, -extents.y), new(extents.x, -extents.y), new(extents.x, extents.y), new(-extents.x, extents.y) };

				shadowCaster.NonpublicSetterWorkaround("m_ShapePath", shapePath);
				shadowCaster.NonpublicSetterWorkaround("m_ShapePathHash", shapePath.GetHashCode());
			}
			++i;
		}

		// determine whether to disallow ladders
		return directionalGates != null && directionalGates.Any(pair => blockerPrefab == pair.m_object); // TODO: don't assume directional gates will never want default ladders?
	}

	private void SpawnKeys(DoorwayInfo doorwayInfo, System.Action<IUnlockable, RoomController, RoomController[]> spawnAction)
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
		LayoutGenerator.Node lockNode = doorwayInfo.ChildRoom == null ? null : GateNodeToChild(doorwayInfo.ChildRoom.m_layoutNodes, LayoutGenerator.Node.Type.Lock);
		RoomController[] keyRooms = doorwayInfo.ChildRoom == null ? new[] { this } : lockNode?.DirectParents.Where(node => node.m_type == LayoutGenerator.Node.Type.Key).Select(node => node.m_room).ToArray();
		spawnAction(unlockable, this, keyRooms == null || keyRooms.Length <= 0 ? null : doorwayInfo.m_excludeSelf.Value ? keyRooms.Where(room => room != this).ToArray() : keyRooms);
	}

	private void OpenDoorway(DoorwayInfo doorwayInfo, bool open)
	{
		Debug.Assert(m_doorwayInfos.Contains(doorwayInfo));

		GameObject doorway = doorwayInfo.m_object;

		// enable/disable doorway
		PlatformEffector2D effector = doorway.GetComponent<PlatformEffector2D>();
		if (effector == null)
		{
			doorway.SetActive(!open);
		}
		else
		{
			// enable effector for dynamic collisions
			effector.enabled = open;
			doorway.GetComponent<Collider2D>().usedByEffector = open;

			// set layer for kinematic movement
			doorway.layer = (open ? GameController.Instance.m_layerOneWay : GameController.Instance.m_layerDefault).ToIndex();

			// change color/shadows for user visibility
			// TODO: match wall texture?
			SpriteRenderer renderer = doorway.GetComponent<SpriteRenderer>();
			renderer.color = open ? m_oneWayPlatformColor : m_wallColor;
			renderer.sprite = open ? m_floorPlatformSprite : m_wallInfo.m_sprite;
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

	private AStarPath RoomPath(Vector2 startPos, Vector2 endPos, ObstructionCheck obstructionChecking, float extentY = -1.0f, float upwardMax = float.MaxValue, float incrementDegrees = -1.0f)
	{
		Debug.Assert(Bounds.Contains(startPos));

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
				if (roomNext == null || info.IsObstructed(obstructionChecking))
				{
					continue;
				}

				// NOTE that we don't immediately break if roomNext is endRoom, since there can be multiple valid paths and we're only guaranteed to have the shortest length when popping from paths[]

				Vector2 posPrev = pathItr.m_pathPositions.Last();
				List<Vector2> connectionPoints = new() { posPrev, info.m_object.transform.position * 2 - info.m_infoReverse.m_object.transform.position, info.m_infoReverse.m_object.transform.position * 2 - info.m_object.transform.position }; // NOTE that we don't use Connection() since we want this particular doorway and have already done obstruction checking // NOTE that we push each point into the room a little to prevent paths running inside walls // TODO: use bbox edge furthest from other object?

				// edit y-coordinate at doorways to match character midpoint
				// TODO: param for flying characters / wires to use the top of the doorway if it's closer
				if (extentY >= 0.0f)
				{
					float doorwayDirY = info.DirectionOutward().y;
					float yAdjustment = doorwayDirY == 0.0f ? roomCur.transform.position.y + extentY : extentY * doorwayDirY; // TODO: don't assume all horizontal doors are at floor height?
					for (int i = 1; i < connectionPoints.Count; ++i) // NOTE that we skip over posPrev
					{
						connectionPoints[i] = new(connectionPoints[i].x, doorwayDirY == 0.0f ? yAdjustment : connectionPoints[i].y + yAdjustment * (i == 0 ? -1 : 1));
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
				List<RoomController> roomPathNew = new(pathItr.m_pathRooms);
				roomPathNew.Add(roomNext);
				List<Vector2> posPathNew = new(pathItr.m_pathPositions);
				connectionPoints.RemoveAt(0); // since this is redundant w/ posPathNew.Last()
				posPathNew.AddRange(connectionPoints);

				// TODO: weight distances based on jumps/traversal required?
				paths.Push(new() { m_pathRooms = roomPathNew, m_pathPositions = posPathNew, m_distanceCur = distanceCurNew, m_distanceTotalEst = distanceCurNew + posNew.ManhattanDistance(endPos) });
			}
		}

		return pathItr; // TODO: find path to closest reachable point if pathItr is null?
	}

	private static int RestrictAngleTo(List<Vector2> connectionPoints, int idx, float incrementRadians)
	{
		// measure unedited segment against desired increment
		Vector2 start = connectionPoints[idx - 1];
		Vector2 diff = connectionPoints[idx] - start;
		float diffRadians = Mathf.Atan2(diff.y, diff.x);
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

	private Vector2[] Connection(RoomController to, ObstructionCheck obstructionChecking, Vector2 priorityPos)
	{
		bool foundConnection = false;
		foreach (DoorwayInfo info in m_doorwayInfos.OrderBy(info => Vector2.Distance(priorityPos, info.m_object.transform.position)))
		{
			if (info.ConnectedRoom != to || info.m_infoReverse.ConnectedRoom != this)
			{
				continue;
			}
			foundConnection = true;
			if (info.IsObstructed(obstructionChecking) || info.m_infoReverse.IsObstructed(obstructionChecking, true))
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
