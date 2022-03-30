using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;


public class RoomController : MonoBehaviour
{
	[System.Serializable]
	public struct DirectionalDoors
	{
		public Vector2 m_direction;
		public WeightedObject<GameObject>[] m_prefabs;
	};


	public WeightedObject<GameObject>[] m_doorInteractPrefabs;
	public string m_doorInteractSceneName = "MainScene"; // TODO: determine dynamically?

	public WeightedObject<GameObject>[] m_npcPrefabs;

	public WeightedObject<GameObject>[] m_doorPrefabs;
	public DirectionalDoors[] m_doorDirectionalPrefabs;
	public WeightedObject<GameObject>[] m_doorSecretPrefabs;

	public GameObject m_doorSealVFX;

	public WeightedObject<GameObject>[] m_spawnPointPrefabs;
	public int m_spawnPointsMax = 4;

	public GameObject m_backdrop;

	public GameObject[] m_doorways;
	public GameObject[] m_walls;

	public GameObject m_ladderRungPrefab;
	public float m_ladderRungSkewMax = 0.2f;


	public static readonly Color m_oneWayPlatformColor = new(0.3f, 0.2f, 0.1f);


	private struct DoorwayInfo
	{
		public RoomController m_parentRoom;
		public RoomController m_childRoom; // TODO: make bidirectional?
		public GameObject m_blocker;
	}
	private /*readonly*/ DoorwayInfo[] m_doorwayInfos; // TODO: combine w/ m_doorways[]?

	private /*readonly*/ Bounds m_bounds;

	private /*readonly*/ LayoutGenerator.Node[] m_layoutNodes;

	private /*readonly*/ GameObject[] m_spawnPoints;

	private /*readonly*/ RoomType m_roomType = null;


	private void Awake()
	{
		m_doorwayInfos = new DoorwayInfo[m_doorways.Length];
		m_bounds = m_backdrop.GetComponent<SpriteRenderer>().bounds;
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (m_layoutNodes == null || ConsoleCommands.LayoutDebugLevel == (int)ConsoleCommands.LayoutDebugLevels.None)
		{
			return;
		}

		Vector3 centerPosItr = m_bounds.center;
		if (m_roomType != null)
		{
			UnityEditor.Handles.Label(centerPosItr, m_roomType.ToString()); // TODO: prevent drift from Scene camera?
		}

		foreach (LayoutGenerator.Node node in m_layoutNodes)
		{
			centerPosItr.y -= 1.0f;
			UnityEditor.Handles.Label(centerPosItr, node.m_type.ToString()); // TODO: prevent drift from Scene camera?

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
	}
#endif


	public void Initialize(LayoutGenerator.Node[] layoutNodes)
	{
		Assert.IsNull(m_layoutNodes);
		m_layoutNodes = layoutNodes;
		foreach (LayoutGenerator.Node node in m_layoutNodes)
		{
			Debug.Assert(node.m_room == null);
			node.m_room = this;
		}

		for (int doorwayIdx = 0; doorwayIdx < m_doorways.Length; ++doorwayIdx)
		{
			if (m_doorwayInfos[doorwayIdx].m_parentRoom != null)
			{
				// open doorway to parent
				OpenDoorway(doorwayIdx, true, DoorwayDirection(doorwayIdx).y > 0.0f);
			}
		}

		// room type
		// TODO: more deliberate choice?
		m_roomType = GameController.Instance.m_roomTypes.RandomWeighted();
		if (m_roomType.m_backdrops != null && m_roomType.m_backdrops.Length > 0)
		{
			RoomType.BackdropInfo backdrop = m_roomType.m_backdrops.RandomWeighted();
			SpriteRenderer renderer = m_backdrop.GetComponent<SpriteRenderer>();
			renderer.sprite = backdrop.m_sprite;
			renderer.color = Utility.ColorRandom(backdrop.m_colorMin, backdrop.m_colorMax);
		}

		// color walls based on area
		// TODO: slight variation?
		Color roomColor = m_layoutNodes.First().AreaParent.m_color; // NOTE that all nodes w/i a single room should have the same area parent
		foreach (GameObject door in m_doorways)
		{
			PlatformEffector2D platform = door.GetComponent<PlatformEffector2D>();
			if (platform != null && platform.enabled)
			{
				continue; // ignore one-way platforms
			}
			door.GetComponent<SpriteRenderer>().color = roomColor;
		}
		foreach (GameObject wall in m_walls)
		{
			wall.GetComponent<SpriteRenderer>().color = roomColor;
		}

		// TODO: prevent overlap of spawned prefabs

		// spawn enemy spawn points
		m_spawnPoints = new GameObject[Random.Range(1, m_spawnPointsMax + 1)]; // TODO: base on room size?
		for (int spawnIdx = 0; spawnIdx < m_spawnPoints.Length; ++spawnIdx)
		{
			Vector3 spawnPosBG = InteriorPosition(float.MaxValue) + Vector3.forward; // NOTE that we don't account for spawn point or enemy size, relying on KinematicObject's spawn checks to prevent getting stuck in walls
			m_spawnPoints[spawnIdx] = Instantiate(m_spawnPointPrefabs.RandomWeighted(), spawnPosBG, Quaternion.Euler(0.0f, 0.0f, Random.Range(0.0f, 360.0f)), transform);
		}

		// spawn node-specific architecture
		bool emptyRoom = false;
		foreach (LayoutGenerator.Node node in m_layoutNodes)
		{
			switch (node.m_type)
			{
				case LayoutGenerator.Node.Type.Entrance:
				case LayoutGenerator.Node.Type.Zone1Door:
					InteractSimple door = Instantiate(m_doorInteractPrefabs.RandomWeighted(), (node.m_type == LayoutGenerator.Node.Type.Entrance ? transform.position : InteriorPosition(0.0f, 0.0f)) + Vector3.forward, Quaternion.identity, transform).GetComponent<InteractSimple>();
					if (node.m_type != LayoutGenerator.Node.Type.Entrance)
					{
						door.m_sceneName = m_doorInteractSceneName;
						emptyRoom = true;
					}
					break;

				case LayoutGenerator.Node.Type.Npc:
					Instantiate(m_npcPrefabs.RandomWeighted(), InteriorPosition(0.0f, 0.0f), Quaternion.identity);
					break;

				default:
					break;
			}
		}
		if (emptyRoom)
		{
			return;
		}

		// spawn furniture
		GameObject furniturePrefab = m_roomType.m_furniturePrefabs.RandomWeighted();
		BoxCollider2D furnitureCollider = furniturePrefab.GetComponent<BoxCollider2D>();
		float furnitureExtentY = furnitureCollider.size.y * 0.5f + furnitureCollider.edgeRadius - furnitureCollider.offset.y;
		GameObject furniture = Instantiate(furniturePrefab, transform); // NOTE that we have to spawn before placement due to size randomization in Awake() // TODO: guarantee size will fit in available space?
		Bounds furnitureBounds = furniture.GetComponent<Collider2D>().bounds;
		for (int failsafeCount = 0; failsafeCount < 100; ++failsafeCount)
		{
			Vector3 spawnPos = new Vector3(m_bounds.center.x, transform.position.y, transform.position.z) + new Vector3(Random.Range(-m_bounds.extents.x + furnitureBounds.extents.x, m_bounds.extents.x - furnitureBounds.extents.x), furnitureExtentY, 1.0f);
			if (Physics2D.OverlapBox(spawnPos + furnitureBounds.center + new Vector3(0.0f, 0.1f, 0.0f), furnitureBounds.size, 0.0f) != null) // NOTE the small offset to avoid collecting the floor; also that this collects our newly spawned furniture when at the origin, but that's okay since keeping the start point clear isn't objectionable
			{
				continue; // re-place and try again
			}
			furniture.transform.position = spawnPos;
			furniture.GetComponent<FurnitureController>().SpawnItems(System.Array.Exists(m_layoutNodes, node => node.m_type == LayoutGenerator.Node.Type.BonusItems), m_roomType);
			furniture = null;
			break;
		}
		if (furniture != null)
		{
			furniture.SetActive(false); // to prevent being visible for a frame while waiting to despawn
			Simulation.Schedule<ObjectDespawn>().m_object = furniture;
		}

		// spawn decoration(s)
		// TODO: prioritize by area?
		int numDecorations = Random.Range(m_roomType.m_decorationsMin, m_roomType.m_decorationsMax + 1);
		Color decoColor = roomColor * 2.0f; // TODO?
		for (int i = 0; i < numDecorations; ++i)
		{
			Vector3 spawnPos = InteriorPosition(Random.Range(m_roomType.m_decorationHeightMin, m_roomType.m_decorationHeightMax)) + Vector3.forward; // TODO: uniform height per room?
			// TODO: prevent overlap
			GameObject decoration = Instantiate(m_roomType.m_decorationPrefabs.RandomWeighted(), spawnPos, Quaternion.identity, transform);

			foreach (SpriteRenderer renderer in decoration.GetComponentsInChildren<SpriteRenderer>(true))
			{
				renderer.color = decoColor * 2.0f; // TODO: unhardcode? vary?
				renderer.flipX = Random.Range(0, 2) != 0;
			}
			foreach (Light2D renderer in decoration.GetComponentsInChildren<Light2D>(true))
			{
				renderer.color = decoColor;
				renderer.intensity = Random.Range(0.0f, 1.0f); // TODO: base on area/progress?
			}
		}
	}

	public void SpawnKeysRecursive()
	{
		foreach (DoorwayInfo doorwayInfo in m_doorwayInfos)
		{
			if (doorwayInfo.m_childRoom == null)
			{
				continue;
			}

			doorwayInfo.m_childRoom.SpawnKeysRecursive();

			LayoutGenerator.Node lockNode = GateNodeToChild(LayoutGenerator.Node.Type.Lock, doorwayInfo.m_childRoom.m_layoutNodes);
			if (lockNode == null)
			{
				continue;
			}

			doorwayInfo.m_blocker.GetComponent<IUnlockable>().SpawnKeys(this, lockNode.DirectParents.Where(node => node.m_type == LayoutGenerator.Node.Type.Key).Select(node => node.m_room).ToArray());
		}
	}

	public RoomController RoomFromPosition(Vector2 position)
	{
		// TODO: efficiency?
		Vector3 pos3D = position;
		pos3D.z = m_bounds.center.z;
		if (m_bounds.Contains(pos3D))
		{
			return this;
		}
		foreach (DoorwayInfo info in m_doorwayInfos)
		{
			if (info.m_childRoom == null)
			{
				// TODO: allow searching parents, too?
				continue;
			}
			RoomController childRoom = info.m_childRoom.RoomFromPosition(position);
			if (childRoom != null)
			{
				return childRoom;
			}
		}
		return null;
	}

	public Vector3 InteriorPosition(float heightMax)
	{
		return InteriorPosition(0.0f, heightMax);
	}

	public Vector3 InteriorPosition(float heightMin, float heightMax)
	{
		Bounds boundsInterior = m_bounds;
		boundsInterior.Expand(new Vector3(-1.0f, -1.0f, 0.0f)); // TODO: dynamically determine wall thickness?

		float xDiffMax = boundsInterior.extents.x;
		float yMaxFinal = Mathf.Min(heightMax, boundsInterior.size.y); // TODO: also count furniture surfaces as "floor"

		return new(boundsInterior.center.x + Random.Range(-xDiffMax, xDiffMax), transform.position.y + Random.Range(heightMin, yMaxFinal), transform.position.z); // NOTE the assumptions that the object position is on the floor of the room but not necessarily centered
	}

	public Vector3 SpawnPointRandom()
	{
		Vector3 pos = m_spawnPoints[Random.Range(0, m_spawnPoints.Length)].transform.position;
		pos.z = transform.position.z; // NOTE that we instantiate the visuals in the background but make sure spawning is at the same depth as the room
		return pos;
	}

	public List<RoomController> ChildRoomPath(Vector2 startPosition, Vector2 endPosition, bool unobstructed)
	{
		// TODO: efficiency? handle multiple possible paths?
		// find root-->start and root-->end paths
		List<RoomController> startPath = RoomPathFromRoot(startPosition, new());
		if (startPath == null)
		{
			return null; // TODO: find closest reachable point?
		}
		List<RoomController> endPath = RoomPathFromRoot(endPosition, new());
		if (endPath == null)
		{
			return null; // TODO: find closest reachable point?
		}

		// remove shared trunk
		RoomController lastSharedRoom = null;
		while (startPath.Count > 0 && endPath.Count > 0 && startPath.First() == endPath.First())
		{
			lastSharedRoom = startPath.First();
			startPath.RemoveAt(0);
			endPath.RemoveAt(0);
		}
		Assert.IsNotNull(lastSharedRoom); // at least the root room should always be shared

		// combine paths in order
		startPath.Reverse();
		startPath.Add(lastSharedRoom);
		startPath.AddRange(endPath);

		if (unobstructed)
		{
			// check for obstructions
			for (int i = 0; i < startPath.Count - 1; ++i)
			{
				if (startPath[i].RoomConnection(startPath[i + 1], true) == null)
				{
					return null;
				}
			}
		}

		return startPath;
	}

	public List<Vector2> ChildPositionPath(Vector2 startPosition, Vector2 endPositionPreoffset, Vector2 offsetMag, bool unobstructed)
	{
		List<RoomController> roomPath = ChildRoomPath(startPosition, endPositionPreoffset, unobstructed);
		if (roomPath == null)
		{
			return null;
		}

		// convert rooms to waypoints
		List<Vector2> waypointPath = new();
		for (int i = 0, n = roomPath.Count - 1; i < n; ++i)
		{
			Vector2[] connectionPoints = roomPath[i].RoomConnection(roomPath[i + 1], unobstructed);
			Assert.IsTrue(connectionPoints.Length > 0 && !connectionPoints.Contains(Vector2.zero));
			waypointPath.AddRange(connectionPoints);
		}

		// add valid end point
		float semifinalX = waypointPath.Count > 0 ? waypointPath.Last().x : startPosition.x;
		Vector2 endPos = endPositionPreoffset + (semifinalX >= endPositionPreoffset.x ? offsetMag : new Vector2(-offsetMag.x, offsetMag.y));
		Bounds endRoomBounds = roomPath.Last().m_bounds;
		endRoomBounds.Expand(new Vector3(-1.0f, -1.0f, 0.0f)); // TODO: dynamically determine wall thickness?
		waypointPath.Add(endRoomBounds.Contains(new(endPos.x, endPos.y, endRoomBounds.center.z)) ? endPos : endRoomBounds.ClosestPoint(endPos)); // TODO: flip offset if closest interior point is significantly different from endPos?

		return waypointPath;
	}

	public bool SpawnChildRoom(GameObject roomPrefab, LayoutGenerator.Node[] layoutNodes, Vector2[] allowedDirections = null)
	{
		// prevent putting keys behind their lock
		// NOTE that we check all nodes' depth even though all nodes w/i a single room should be at the same depth
		if (layoutNodes.Max(node => node.Depth) < m_layoutNodes.Min(node => node.Depth))
		{
			return false;
		}

		bool success = DoorwaysRandomOrder(i =>
		{
			if (m_doorwayInfos[i].m_childRoom != null || m_doorwayInfos[i].m_parentRoom != null)
			{
				return false;
			}

			// maybe replace/remove
			GameObject doorway = m_doorways[i];
			MaybeReplaceDoor(i, roomPrefab, layoutNodes, allowedDirections);
			return m_doorwayInfos[i].m_childRoom != null;
		});

		if (success)
		{
			return true;
		}

		bool requireImmediateChild = System.Array.Exists(layoutNodes, node => System.Array.Exists(m_layoutNodes, parentNode => (parentNode.m_type == LayoutGenerator.Node.Type.Lock || parentNode.m_type == LayoutGenerator.Node.Type.Secret) && parentNode == node.TightCoupleParent));
		if (requireImmediateChild)
		{
			return false;
		}

		// try spawning from children
		return DoorwaysRandomOrder(i =>
		{
			DoorwayInfo doorway = m_doorwayInfos[i];
			if (doorway.m_childRoom == null)
			{
				return false;
			}
			return doorway.m_childRoom.SpawnChildRoom(roomPrefab, layoutNodes, allowedDirections);
		});
	}

	public void SpawnLadder(GameObject doorway)
	{
		Assert.IsTrue(m_doorways.Contains(doorway) || System.Array.Exists(m_doorwayInfos, info => info.m_blocker == doorway));

		// determine rung count/height
		float yTop = doorway.transform.position.y - 1.5f; // TODO: base top distance on character height
		float heightDiff = yTop - transform.position.y; // TODO: don't assume pivot point is always the place to stop?
		int rungCount = Mathf.RoundToInt(heightDiff / m_ladderRungPrefab.GetComponent<BoxCollider2D>().size.y);
		float rungHeight = heightDiff / rungCount;

		Vector3 posItr = doorway.transform.position;
		posItr.y = yTop - rungHeight;
		for (int i = 0; i < rungCount; ++i)
		{
			// create and resize
			GameObject ladder = Instantiate(m_ladderRungPrefab, posItr, Quaternion.identity, transform);
			SpriteRenderer renderer = ladder.GetComponent<SpriteRenderer>();
			renderer.size = new Vector2(renderer.size.x, rungHeight);
			BoxCollider2D collider = ladder.GetComponent<BoxCollider2D>();
			collider.size = new Vector2(collider.size.x, rungHeight);
			collider.offset = new Vector2(collider.offset.x, rungHeight * 0.5f);

			// iterate
			posItr.x += Random.Range(-m_ladderRungSkewMax, m_ladderRungSkewMax); // TODO: guarantee AI navigability? clamp to room size?
			posItr.y -= rungHeight;
		}
	}

	public void SealRoom(bool seal)
	{
		for (int i = 0; i < m_doorways.Length; ++i)
		{
			if (m_doorwayInfos[i].m_childRoom == null && m_doorwayInfos[i].m_parentRoom == null)
			{
				continue;
			}

			OpenDoorway(i, !seal, false);

			if (seal && m_doorwayInfos[i].m_blocker == null)
			{
				Vector2 doorwaySize = DoorwaySize(m_doorways[i]);
				VisualEffect vfx = Instantiate(m_doorSealVFX, m_doorways[i].transform.position + new Vector3(0.0f, -0.5f * doorwaySize.y, 0.0f), Quaternion.identity).GetComponent<VisualEffect>();
				vfx.SetVector3("StartAreaSize", new Vector3(doorwaySize.x, 0.0f, 0.0f));
			}
		}

		GetComponent<AudioSource>().Play();
		// TODO: animation?
	}


	private Vector2 DoorwaySize(GameObject doorway) => doorway.GetComponent<BoxCollider2D>().size * doorway.transform.localScale; // NOTE that we can't use Collider2D.bounds since this can be called before physics has run

	private Vector2 DoorwayDirection(int index)
	{
		GameObject doorway = m_doorways[index];
		Vector2 pivotToDoorway = (Vector2)doorway.transform.position - (Vector2)transform.position;
		Vector3 doorwaySize = DoorwaySize(doorway);
		return doorwaySize.x > doorwaySize.y ? new Vector2(0.0f, Mathf.Sign(pivotToDoorway.y)) : new Vector2(Mathf.Sign(pivotToDoorway.x), 0.0f);
	}

	private int[] DoorwayReverseIndices(Vector2 replaceDirection)
	{
		List<int> indices = new();
		for (int i = 0; i < m_doorways.Length; ++i)
		{
			GameObject doorway = m_doorways[i];
			Vector3 doorwaySize = DoorwaySize(doorway);
			if (Vector2.Dot((Vector2)transform.position - (Vector2)doorway.transform.position, replaceDirection) > 0.0f && doorwaySize.x > doorwaySize.y == (Mathf.Abs(replaceDirection.x) < Mathf.Abs(replaceDirection.y))) // TODO: better way of determining reverse direction doorway?
			{
				indices.Add(i);
			}
		}
		return indices.ToArray();
	}

	private bool DoorwaysRandomOrder(System.Func<int, bool> f)
	{
		int[] order = Enumerable.Range(0, m_doorwayInfos.Length).OrderBy(i => Random.value).ToArray();
		foreach (int i in order)
		{
			bool done = f(i);
			if (done)
			{
				return true;
			}
		}
		return false;
	}

	private LayoutGenerator.Node GateNodeToChild(LayoutGenerator.Node.Type gateType, LayoutGenerator.Node[] childNodes)
	{
		return m_layoutNodes.FirstOrDefault(node => node.m_type == gateType && System.Array.Exists(childNodes, childNode => childNode.DirectParents.Exists(childParentNode => node == childParentNode)));
	}

	// TODO: inline?
	private void MaybeReplaceDoor(int index, GameObject roomPrefab, LayoutGenerator.Node[] childNodes, Vector2[] allowedDirections)
	{
		Vector2 replaceDirection = DoorwayDirection(index);
		if (allowedDirections != null && !allowedDirections.Contains(replaceDirection))
		{
			return;
		}

		GameObject doorway = m_doorways[index];
		ref DoorwayInfo doorwayInfo = ref m_doorwayInfos[index];
		Assert.IsNull(doorwayInfo.m_parentRoom);
		Assert.IsNull(doorwayInfo.m_childRoom);
		Assert.AreApproximatelyEqual(replaceDirection.magnitude, 1.0f);

		// determine child position
		RoomController otherRoom = roomPrefab.GetComponent<RoomController>();
		Bounds childBounds = otherRoom.m_backdrop.GetComponent<SpriteRenderer>().bounds; // NOTE that we can't use m_bounds since uninstantiated prefabs don't have Awake() called on them
		Vector2 doorwayPos = doorway.transform.position;
		int reverseIdx = -1;
		float doorwayToEdge = Mathf.Min(m_bounds.max.x - doorwayPos.x, m_bounds.max.y - doorwayPos.y, doorwayPos.x - m_bounds.min.x, doorwayPos.y - m_bounds.min.y); // TODO: don't assume convex rooms?
		Vector2 childPivotPos = Vector2.zero;
		Vector2 childPivotToCenter = childBounds.center - roomPrefab.transform.position;
		bool isOpen = false;
		foreach (int idxCandidate in otherRoom.DoorwayReverseIndices(replaceDirection).OrderBy(i => Random.value))
		{
			Vector2 childDoorwayPosLocal = otherRoom.m_doorways[idxCandidate].transform.position;
			float childDoorwayToEdge = Mathf.Min(childBounds.max.x - childDoorwayPosLocal.x, childBounds.max.y - childDoorwayPosLocal.y, childDoorwayPosLocal.x - childBounds.min.x, childDoorwayPosLocal.y - childBounds.min.y);
			childPivotPos = doorwayPos + replaceDirection * (doorwayToEdge + childDoorwayToEdge) - childDoorwayPosLocal;

			// check for obstructions
			isOpen = Physics2D.OverlapBox(childPivotPos + childPivotToCenter, childBounds.size - new Vector3(0.1f, 0.1f, 0.0f), 0.0f) == null; // NOTE the small size reduction to avoid always collecting ourself
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
		doorwayInfo.m_childRoom = childRoom;
		childRoom.m_doorwayInfos[reverseIdx].m_parentRoom = this;

		LayoutGenerator.Node blockerNode = GateNodeToChild(LayoutGenerator.Node.Type.Lock, childNodes);
		bool isLock = blockerNode != null;
		if (!isLock)
		{
			blockerNode = GateNodeToChild(LayoutGenerator.Node.Type.Secret, childNodes);
		}
		bool noLadder = false;
		if (blockerNode != null)
		{
			// create gate
			Assert.IsNull(doorwayInfo.m_blocker);
			WeightedObject<GameObject>[] directionalBlockerPrefabs = isLock ? m_doorDirectionalPrefabs.FirstOrDefault(pair => pair.m_direction == replaceDirection).m_prefabs : null; // TODO: don't assume that secrets will never be directional?
			GameObject blockerPrefab = (isLock ? (directionalBlockerPrefabs != null ? directionalBlockerPrefabs.Concat(m_doorPrefabs).ToArray() : m_doorPrefabs) : m_doorSecretPrefabs).RandomWeighted();
			noLadder = directionalBlockerPrefabs != null && System.Array.Exists(directionalBlockerPrefabs, pair => blockerPrefab == pair.m_object); // TODO: don't assume directional gates will never want default ladders?
			doorwayInfo.m_blocker = Instantiate(blockerPrefab, doorway.transform.position + Vector3.back, Quaternion.identity, transform); // NOTE the depth decrease to ensure rendering on top of platforms
			if (isLock)
			{
				doorwayInfo.m_blocker.GetComponent<IUnlockable>().Parent = gameObject;
			}
			childRoom.m_doorwayInfos[reverseIdx].m_blocker = doorwayInfo.m_blocker;

			// resize gate to fit doorway
			Vector2 size = DoorwaySize(doorway);
			doorwayInfo.m_blocker.GetComponent<BoxCollider2D>().size = size;
			doorwayInfo.m_blocker.GetComponent<SpriteRenderer>().size = size;

			// update shadow caster shape
			ShadowCaster2D shadowCaster = doorwayInfo.m_blocker.GetComponent<ShadowCaster2D>();
			if (shadowCaster != null)
			{
				Vector3 extents = size * 0.5f;
				Vector3[] shapePath = new Vector3[] { new(-extents.x, -extents.y, 0.0f), new(extents.x, -extents.y, 0.0f), new(extents.x, extents.y, 0.0f), new(-extents.x, extents.y, 0.0f) };

				// see https://forum.unity.com/threads/can-2d-shadow-caster-use-current-sprite-silhouette.861256/ for explanation of workaround for non-public setter
				System.Type shadowCasterType = typeof(ShadowCaster2D);
				const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
				System.Reflection.FieldInfo pathSetterWorkaround = shadowCasterType.GetField("m_ShapePath", flags);
				System.Reflection.FieldInfo hashSetterWorkaround = shadowCasterType.GetField("m_ShapePathHash", flags);
				pathSetterWorkaround.SetValue(shadowCaster, shapePath);
				hashSetterWorkaround.SetValue(shadowCaster, shapePath.GetHashCode());
			}
		}

		childRoom.Initialize(childNodes);

		OpenDoorway(index, true, !noLadder && replaceDirection.y > 0.0f);
	}

	private void OpenDoorway(int index, bool open, bool spawnLadders)
	{
		GameObject doorway = m_doorways[index];

		// spawn ladder rungs
		if (spawnLadders && m_ladderRungPrefab != null)
		{
			SpawnLadder(doorway);
		}

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
			doorway.layer = LayerMask.NameToLayer(open ? "OneWayPlatforms" : "Default"); // TODO: cache layer indices?

			// change color/shadows for user visibility
			doorway.GetComponent<SpriteRenderer>().color = open ? m_oneWayPlatformColor : m_layoutNodes.First().AreaParent.m_color; // TODO: cache room color?
			doorway.GetComponent<ShadowCaster2D>().enabled = !open;
		}

		if (!open)
		{
			// move any newly colliding objects into room
			Vector2 doorwaySize = DoorwaySize(doorway);
			Collider2D[] colliders = Physics2D.OverlapBoxAll(doorway.transform.position, doorwaySize, 0.0f);
			Vector2 intoRoom = -DoorwayDirection(index);
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

	private List<RoomController> RoomPathFromRoot(Vector2 endPosition, List<RoomController> prePath)
	{
		// TODO: A* algorithm?

		prePath = new(prePath); // NOTE the copy to prevent storing up entries from other branches of the recursion
		prePath.Add(this);

		Vector3 pos3D = new(endPosition.x, endPosition.y, m_bounds.center.z);
		if (m_bounds.Contains(pos3D))
		{
			return prePath;
		}

		// check non-null children
		// TODO: prioritize by distance from endPosition?
		foreach (DoorwayInfo info in m_doorwayInfos.Where(info => info.m_childRoom != null))
		{
			List<RoomController> childPath = info.m_childRoom.RoomPathFromRoot(endPosition, prePath);
			if (childPath != null)
			{
				return childPath;
			}
		}

		return null;
	}

	private Vector2[] RoomConnection(RoomController to, bool unobstructed)
	{
		// TODO: efficiency?
		Vector2[] connectionPoints = new Vector2[2];
		int outputIdx = 0;
		foreach (System.Tuple<DoorwayInfo, GameObject>[] infoArray in new System.Tuple<DoorwayInfo, GameObject>[][] { m_doorwayInfos.Zip(m_doorways, System.Tuple.Create).ToArray(), to.m_doorwayInfos.Zip(to.m_doorways, System.Tuple.Create).ToArray() })
		{
			foreach (System.Tuple<DoorwayInfo, GameObject> pair in infoArray)
			{
				DoorwayInfo info = pair.Item1;
				if (info.m_childRoom != to && info.m_parentRoom != to && info.m_childRoom != this && info.m_parentRoom != this)
				{
					continue;
				}

				if (unobstructed)
				{
					if (info.m_blocker != null)
					{
						return null;
					}
					if (pair.Item2.activeSelf)
					{
						PlatformEffector2D oneway = pair.Item2.GetComponent<PlatformEffector2D>();
						if (oneway == null || !oneway.enabled)
						{
							return null;
						}
					}
				}

				connectionPoints[outputIdx] = pair.Item2.transform.position;
				break;
			}
			++outputIdx;
		}

		return connectionPoints;
	}
}
