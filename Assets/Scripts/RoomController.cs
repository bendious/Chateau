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

	public WeightedObject<GameObject>[] m_npcPrefabs;

	public WeightedObject<GameObject>[] m_doorPrefabs;
	public DirectionalDoors[] m_doorDirectionalPrefabs;
	public WeightedObject<GameObject>[] m_doorSecretPrefabs;

	public GameObject m_doorSealVFX;

	public WeightedObject<GameObject>[] m_spawnPointPrefabs;
	public int m_spawnPointsMax = 4;

	public GameObject m_backdrop;

	public GameObject[] m_walls;

	public WeightedObject<GameObject>[] m_ladderRungPrefabs;
	public float m_ladderRungSkewMax = 0.2f;


	public static readonly Color m_oneWayPlatformColor = new(0.3f, 0.2f, 0.1f);


	public GameObject[] Doorways => m_doorwayInfos.Select(info => info.m_object).ToArray();

	public Bounds BoundsInterior { get {
		Bounds boundsInterior = m_bounds; // NOTE the copy since Expand() modifies the given struct
		boundsInterior.Expand(new Vector3(-1.0f, -1.0f, float.MaxValue)); // TODO: dynamically determine wall thickness?
		return boundsInterior;
	} }


	[System.Serializable]
	private class DoorwayInfo
	{
		public /*readonly*/ GameObject m_object;

		public RoomController ParentRoom
		{
			get => m_connectionType != ConnectionType.Parent ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.Parent; ConnectedRoom = value; }
		}
		public RoomController ChildRoom
		{
			get => m_connectionType != ConnectionType.Child ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.Child; ConnectedRoom = value; }
		}
		public RoomController SiblingRoom
		{
			get => m_connectionType != ConnectionType.Sibling ? null : ConnectedRoom;
			set { Debug.Assert(ConnectedRoom == null || value == null); m_connectionType = ConnectionType.Sibling; ConnectedRoom = value; }
		}
		public RoomController ConnectedRoom { get; private set; }

		public GameObject m_blocker;

		private enum ConnectionType { None, Parent, Child, Sibling };
		private ConnectionType m_connectionType;
	}
	[SerializeField]
	private /*readonly*/ DoorwayInfo[] m_doorwayInfos;

	private /*readonly*/ Bounds m_bounds;

	private /*readonly*/ LayoutGenerator.Node[] m_layoutNodes;

	private /*readonly*/ GameObject[] m_spawnPoints;

	private /*readonly*/ RoomType m_roomType = null;


	private void Awake()
	{
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

	public void FinalizeRecursive()
	{
		for (int doorwayIdx = 0; doorwayIdx < m_doorwayInfos.Length; ++doorwayIdx)
		{
			DoorwayInfo doorwayInfo = m_doorwayInfos[doorwayIdx];
			if (doorwayInfo.ParentRoom != null)
			{
				// open doorway to parent
				OpenDoorway(doorwayIdx, true, DoorwayDirection(doorwayIdx).y > 0.0f);
			}

			if (doorwayInfo.ChildRoom == null)
			{
				// maybe add cutback
				// TODO: prevent softlocks
				if (false)//TODO doorwayInfo.ConnectedRoom == null)
				{
					Vector2 direction = DoorwayDirection(doorwayIdx);
					GameObject doorway = doorwayInfo.m_object;
					doorwayInfo.SiblingRoom = GameController.Instance.RoomFromPosition((Vector2)doorway.transform.position + direction);
					if (doorwayInfo.SiblingRoom != null) // NOTE that all m_layoutNode entries are assumed to have equal depth
					{
						int reverseIdx = -1;
						foreach (int siblingInfoIdx in doorwayInfo.SiblingRoom.DoorwayReverseIndices(direction))
						{
							if (Vector2.Distance(doorwayInfo.SiblingRoom.m_doorwayInfos[siblingInfoIdx].m_object.transform.position, doorway.transform.position) < 1.0f/*?*/)
							{
								reverseIdx = siblingInfoIdx;
								break;
							}
						}
						if (reverseIdx < 0)
						{
							doorwayInfo.SiblingRoom = null;
							continue;
						}

						OpenDoorway(doorwayIdx, true, false);
						doorwayInfo.SiblingRoom.m_doorwayInfos[reverseIdx].SiblingRoom = this;
						doorwayInfo.SiblingRoom.OpenDoorway(reverseIdx, true, false);
						if (doorwayInfo.SiblingRoom.m_layoutNodes.First().Depth != m_layoutNodes.First().Depth)
						{
							// add one-way lock
							SpawnGate(doorwayInfo, true, direction, doorway, doorwayInfo.SiblingRoom, reverseIdx);
						}
					}
				}

				continue;
			}

			doorwayInfo.ChildRoom.FinalizeRecursive();

			IUnlockable unlockable = doorwayInfo.m_blocker == null ? null : doorwayInfo.m_blocker.GetComponent<IUnlockable>();
			int otherDepth = doorwayInfo.ChildRoom != null ? doorwayInfo.ChildRoom.m_layoutNodes.First().Depth : doorwayInfo.SiblingRoom != null ? doorwayInfo.SiblingRoom.m_layoutNodes.First().Depth : int.MaxValue;
			if (unlockable == null || (doorwayInfo.SiblingRoom != null ? m_layoutNodes.First().Depth <= otherDepth : m_layoutNodes.First().Depth > otherDepth))
			{
				continue;
			}

			LayoutGenerator.Node lockNode = GateNodeToChild(LayoutGenerator.Node.Type.Lock, doorwayInfo.ChildRoom.m_layoutNodes);
			unlockable.SpawnKeys(this, lockNode == null ? new RoomController[] { this } : lockNode.DirectParents.Where(node => node.m_type == LayoutGenerator.Node.Type.Key).Select(node => node.m_room).ToArray());
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
		foreach (GameObject door in m_doorwayInfos.Select(doorway => doorway.m_object))
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

		// spawn enemy spawn points
		m_spawnPoints = new GameObject[Random.Range(1, m_spawnPointsMax + 1)]; // TODO: base on room size?
		for (int spawnIdx = 0; spawnIdx < m_spawnPoints.Length; ++spawnIdx)
		{
			GameObject spawnPrefab = m_spawnPointPrefabs.RandomWeighted();
			Vector3 spawnPosBG = InteriorPosition(float.MaxValue, spawnPrefab); // NOTE that we don't account for maximum enemy size, relying on KinematicObject's spawn checks to prevent getting stuck in walls
			m_spawnPoints[spawnIdx] = Instantiate(spawnPrefab, spawnPosBG, Quaternion.Euler(0.0f, 0.0f, Random.Range(0.0f, 360.0f)), transform);
		}

		// spawn node-specific architecture
		bool emptyRoom = false;
		foreach (LayoutGenerator.Node node in m_layoutNodes)
		{
			switch (node.m_type)
			{
				case LayoutGenerator.Node.Type.Entrance:
				case LayoutGenerator.Node.Type.ExitDoor:
					GameObject doorPrefab = m_doorInteractPrefabs.RandomWeighted();
					InteractSimple door = Instantiate(doorPrefab, node.m_type == LayoutGenerator.Node.Type.Entrance ? transform.position : InteriorPosition(0.0f, 0.0f, doorPrefab), Quaternion.identity, transform).GetComponent<InteractSimple>();
					if (node.m_type != LayoutGenerator.Node.Type.Entrance)
					{
						door.m_sceneChange = true;
						emptyRoom = true;
					}
					break;

				case LayoutGenerator.Node.Type.TutorialMove:
				case LayoutGenerator.Node.Type.TutorialAim:
				case LayoutGenerator.Node.Type.TutorialDrop:
				case LayoutGenerator.Node.Type.TutorialJump:
				case LayoutGenerator.Node.Type.TutorialInteract:
				case LayoutGenerator.Node.Type.TutorialUse:
				case LayoutGenerator.Node.Type.TutorialInventory:
				case LayoutGenerator.Node.Type.TutorialThrow:
					GameObject prefab = m_roomType.m_decorationPrefabs[node.m_type - LayoutGenerator.Node.Type.TutorialMove].m_object;
					Instantiate(prefab, InteriorPosition(m_roomType.m_decorationHeightMin, m_roomType.m_decorationHeightMax, prefab), Quaternion.identity, transform);
					if (node.m_type == LayoutGenerator.Node.Type.TutorialInteract)
					{
						prefab = m_roomType.m_itemPrefabs.RandomWeighted();
						Instantiate(prefab, InteriorPosition(m_roomType.m_decorationHeightMin, m_roomType.m_decorationHeightMax, prefab), Quaternion.identity);
					}
					break;

				case LayoutGenerator.Node.Type.Npc:
					GameObject npcPrefab = m_npcPrefabs.RandomWeighted();
					Instantiate(npcPrefab, InteriorPosition(0.0f, 0.0f) + (Vector3)Utility.OriginToCenterY(npcPrefab), Quaternion.identity);
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
		if (m_roomType.m_furniturePrefabs.Length > 0)
		{
			GameObject furniture = Instantiate(m_roomType.m_furniturePrefabs.RandomWeighted(), transform); // NOTE that we have to spawn before placement due to size randomization in Awake() // TODO: guarantee size will fit in available space?
			furniture.transform.position = InteriorPosition(0.0f, furniture);
			furniture.GetComponent<FurnitureController>().SpawnItems(System.Array.Exists(m_layoutNodes, node => node.m_type == LayoutGenerator.Node.Type.BonusItems), m_roomType);
		}

		// spawn decoration(s)
		// TODO: prioritize by area?
		int numDecorations = Random.Range(m_roomType.m_decorationsMin, m_roomType.m_decorationsMax + 1);
		Color decoColor = roomColor * 2.0f; // TODO?
		for (int i = 0; i < numDecorations; ++i)
		{
			GameObject decoPrefab = m_roomType.m_decorationPrefabs.RandomWeighted();
			Vector3 spawnPos = InteriorPosition(Random.Range(m_roomType.m_decorationHeightMin, m_roomType.m_decorationHeightMax), decoPrefab); // TODO: uniform height per room?
			GameObject decoration = Instantiate(decoPrefab, spawnPos, Quaternion.identity, transform);

			foreach (SpriteRenderer renderer in decoration.GetComponentsInChildren<SpriteRenderer>(true))
			{
				renderer.color = decoColor * 2.0f; // TODO: unhardcode? vary?
				//renderer.flipX = Random.Range(0, 2) != 0; // TODO: re-enable for non-text decorations
			}
			foreach (Light2D renderer in decoration.GetComponentsInChildren<Light2D>(true))
			{
				renderer.color = decoColor;
				renderer.intensity = Random.Range(0.0f, 1.0f); // TODO: base on area/progress?
			}
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
			if (info.ChildRoom == null)
			{
				// TODO: allow searching parents, too?
				continue;
			}
			RoomController childRoom = info.ChildRoom.RoomFromPosition(position);
			if (childRoom != null)
			{
				return childRoom;
			}
		}
		return null;
	}

	public Vector3 InteriorPosition(float heightMax, GameObject preventOverlapPrefab = null)
	{
		return InteriorPosition(0.0f, heightMax, preventOverlapPrefab);
	}

	public Vector3 InteriorPosition(float heightMin, float heightMax, GameObject preventOverlapPrefab = null)
	{
		Bounds boundsInterior = BoundsInterior;

		// calculate overlap bbox
		Bounds bboxNew = new();
		if (preventOverlapPrefab != null)
		{
			SpriteRenderer[] renderers = preventOverlapPrefab.GetComponentsInChildren<SpriteRenderer>();
			bboxNew = renderers.First().bounds; // NOTE that we can't assume the local origin should always be included due to being given post-instantiation furniture objects
			foreach (SpriteRenderer renderer in renderers)
			{
				bboxNew.Encapsulate(renderer.bounds);
			}
			bboxNew.Expand(new Vector3(-0.01f, -0.01f, float.MaxValue)); // NOTE the slight x/y contraction to avoid always collecting the floor when up against it
		}

		float xDiffMax = boundsInterior.extents.x - bboxNew.extents.x;
		Debug.Assert(xDiffMax >= 0.0f);
		float yMaxFinal = Mathf.Min(heightMax, boundsInterior.size.y - bboxNew.size.y); // TODO: also count furniture surfaces as "floor"

		Vector3 pos = new(boundsInterior.center.x + Random.Range(-xDiffMax, xDiffMax), transform.position.y + Random.Range(heightMin, yMaxFinal), transform.position.z); // NOTE the assumptions that the object position is on the floor of the room but not necessarily centered
		if (preventOverlapPrefab == null)
		{
			return pos;
		}

		// get points until no overlap
		// TODO: more deliberate iteration? better fallback?
		Vector3 centerOrig = bboxNew.center; // NOTE that we can't assume the bbox is centered
		float xSizeEffective = boundsInterior.size.x - bboxNew.size.x;
		const int attemptsMax = 100;
		int failsafe = attemptsMax;
		do
		{
			bboxNew.center = centerOrig + pos;

			bool overlap = false;
			foreach (SpriteRenderer renderer in GetComponentsInChildren<SpriteRenderer>())
			{
				if (renderer.gameObject != m_backdrop && renderer.bounds.Intersects(bboxNew))
				{
					overlap = true;
					break;
				}
			}
			if (!overlap)
			{
				break;
			}

			pos.x += xSizeEffective / attemptsMax;
			pos.y = transform.position.y + Random.Range(heightMin, yMaxFinal);
			if (pos.x > boundsInterior.max.x - bboxNew.extents.x)
			{
				pos.x -= xSizeEffective;
			}
		}
		while (--failsafe > 0);
		Debug.Assert(failsafe > 0);

		return pos;
	}

	public Vector3 SpawnPointRandom()
	{
		return m_spawnPoints[Random.Range(0, m_spawnPoints.Length)].transform.position;
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

	public RoomController SpawnChildRoom(GameObject roomPrefab, LayoutGenerator.Node[] layoutNodes, Vector2[] allowedDirections = null)
	{
		// prevent putting keys behind their lock
		// NOTE that we check all nodes' depth even though all nodes w/i a single room should be at the same depth
		if (layoutNodes.Max(node => node.Depth) < m_layoutNodes.Min(node => node.Depth))
		{
			return null;
		}

		RoomController childRoom = DoorwaysRandomOrder(i =>
		{
			DoorwayInfo doorway = m_doorwayInfos[i];
			if (doorway.ConnectedRoom != null)
			{
				return null;
			}

			// maybe replace/remove
			MaybeReplaceDoor(i, roomPrefab, layoutNodes, allowedDirections);
			return doorway.ChildRoom;
		});

		if (childRoom != null)
		{
			return childRoom;
		}

		bool requireImmediateChild = System.Array.Exists(layoutNodes, node => System.Array.Exists(m_layoutNodes, parentNode => (parentNode.m_type == LayoutGenerator.Node.Type.Lock || parentNode.m_type == LayoutGenerator.Node.Type.Secret) && parentNode == node.TightCoupleParent));
		if (requireImmediateChild)
		{
			return null;
		}

		// try spawning from children
		return DoorwaysRandomOrder(i =>
		{
			DoorwayInfo doorway = m_doorwayInfos[i];
			if (doorway.ChildRoom == null)
			{
				return null;
			}
			return doorway.ChildRoom.SpawnChildRoom(roomPrefab, layoutNodes, allowedDirections);
		});
	}

	public void SpawnLadder(GameObject doorway, GameObject prefabForced = null, bool spawnBunched = false)
	{
		Assert.IsTrue(System.Array.Exists(m_doorwayInfos, info => info.m_object == doorway || info.m_blocker == doorway));

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
		posItr.y = yTop - (hanging ? 0.0f : rungHeightTotal);
		Rigidbody2D bodyPrev = null;
		for (int i = 0; i < rungCount; ++i)
		{
			// create
			GameObject ladder = Instantiate(ladderRungPrefab, posItr, Quaternion.identity, transform);

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
				renderer.size = new Vector2(renderer.size.x, rungHeightTotal);
				BoxCollider2D collider = ladder.GetComponent<BoxCollider2D>();
				collider.size = new Vector2(collider.size.x, rungHeightTotal);
				collider.offset = new Vector2(collider.offset.x, rungHeightTotal * 0.5f);
			}

			// iterate
			posItr.x += Random.Range(-m_ladderRungSkewMax, m_ladderRungSkewMax); // TODO: guarantee AI navigability? clamp to room size?
			posItr.y -= spawnBunched ? rungOnlyHeight : rungHeightTotal;
		}
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

			bool wasOpen = DoorwayIsOpen(i);
			OpenDoorway(i, !seal, false);

			if (seal && wasOpen)
			{
				GameObject doorway = doorwayInfo.m_object;
				Vector2 doorwaySize = DoorwaySize(doorway);
				VisualEffect vfx = Instantiate(m_doorSealVFX, doorway.transform.position + new Vector3(0.0f, -0.5f * doorwaySize.y, 0.0f), Quaternion.identity).GetComponent<VisualEffect>();
				vfx.SetVector3("StartAreaSize", new Vector3(doorwaySize.x, 0.0f, 0.0f));

				doorway.GetComponent<AudioSource>().Play();
				// TODO: animation?
			}
		}
	}


	private Vector2 DoorwaySize(GameObject doorway) => doorway.GetComponent<BoxCollider2D>().size * doorway.transform.localScale; // NOTE that we can't use Collider2D.bounds since this can be called before physics has run

	private Vector2 DoorwayDirection(int index)
	{
		GameObject doorway = m_doorwayInfos[index].m_object;
		Vector2 pivotToDoorway = (Vector2)doorway.transform.position - (Vector2)transform.position;
		Vector3 doorwaySize = DoorwaySize(doorway);
		return doorwaySize.x > doorwaySize.y ? new Vector2(0.0f, Mathf.Sign(pivotToDoorway.y)) : new Vector2(Mathf.Sign(pivotToDoorway.x), 0.0f);
	}

	private int[] DoorwayReverseIndices(Vector2 replaceDirection)
	{
		List<int> indices = new();
		for (int i = 0; i < m_doorwayInfos.Length; ++i)
		{
			GameObject doorway = m_doorwayInfos[i].m_object;
			Vector3 doorwaySize = DoorwaySize(doorway);
			if (Vector2.Dot((Vector2)transform.position - (Vector2)doorway.transform.position, replaceDirection) > 0.0f && doorwaySize.x > doorwaySize.y == (Mathf.Abs(replaceDirection.x) < Mathf.Abs(replaceDirection.y))) // TODO: better way of determining reverse direction doorway?
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

		DoorwayInfo doorwayInfo = m_doorwayInfos[index];
		GameObject doorway = doorwayInfo.m_object;
		Assert.IsNull(doorwayInfo.ConnectedRoom);
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
			Vector2 childDoorwayPosLocal = otherRoom.m_doorwayInfos[idxCandidate].m_object.transform.position;
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
		doorwayInfo.ChildRoom = childRoom;
		DoorwayInfo reverseDoorwayInfo = childRoom.m_doorwayInfos[reverseIdx];
		reverseDoorwayInfo.ParentRoom = this;

		LayoutGenerator.Node blockerNode = GateNodeToChild(LayoutGenerator.Node.Type.Lock, childNodes);
		bool isLock = blockerNode != null;
		if (!isLock)
		{
			blockerNode = GateNodeToChild(LayoutGenerator.Node.Type.Secret, childNodes);
		}
		bool noLadder = false;
		if (blockerNode != null)
		{
			noLadder = SpawnGate(doorwayInfo, isLock, replaceDirection, doorway, childRoom, reverseIdx);
		}

		childRoom.SetNodes(childNodes);

		OpenDoorway(index, true, !noLadder && replaceDirection.y > 0.0f);
	}

	// TODO: clean up signature
	private bool SpawnGate(DoorwayInfo doorwayInfo, bool isLock, Vector2 replaceDirection, GameObject doorway, RoomController otherRoom, int reverseIdx)
	{
		// create gate
		Assert.IsNull(doorwayInfo.m_blocker);
		WeightedObject<GameObject>[] directionalBlockerPrefabs = isLock ? m_doorDirectionalPrefabs.FirstOrDefault(pair => pair.m_direction == replaceDirection).m_prefabs : null; // TODO: don't assume that secrets will never be directional?
		GameObject blockerPrefab = (isLock ? (directionalBlockerPrefabs != null ? directionalBlockerPrefabs.Concat(m_doorPrefabs).ToArray() : m_doorPrefabs) : m_doorSecretPrefabs).RandomWeighted();
		doorwayInfo.m_blocker = Instantiate(blockerPrefab, doorway.transform.position, Quaternion.identity, transform);
		if (isLock)
		{
			doorwayInfo.m_blocker.GetComponent<IUnlockable>().Parent = gameObject;
		}
		otherRoom.m_doorwayInfos[reverseIdx].m_blocker = doorwayInfo.m_blocker;

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

		return directionalBlockerPrefabs != null && System.Array.Exists(directionalBlockerPrefabs, pair => blockerPrefab == pair.m_object); // TODO: don't assume directional gates will never want default ladders?
	}

	private void OpenDoorway(int index, bool open, bool spawnLadders)
	{
		GameObject doorway = m_doorwayInfos[index].m_object;

		// spawn ladder rungs
		if (spawnLadders && m_ladderRungPrefabs != null && m_ladderRungPrefabs.Length > 0)
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
		foreach (DoorwayInfo info in m_doorwayInfos.Where(info => info.ChildRoom != null))
		{
			List<RoomController> childPath = info.ChildRoom.RoomPathFromRoot(endPosition, prePath);
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
		foreach (DoorwayInfo[] infoArray in new DoorwayInfo[][] { m_doorwayInfos, to.m_doorwayInfos })
		{
			foreach (DoorwayInfo info in infoArray)
			{
				if (info.ConnectedRoom != to && info.ConnectedRoom != this)
				{
					continue;
				}

				if (unobstructed)
				{
					if (info.m_blocker != null)
					{
						return null;
					}
					if (info.m_object.activeSelf)
					{
						PlatformEffector2D oneway = info.m_object.GetComponent<PlatformEffector2D>();
						if (oneway == null || !oneway.enabled)
						{
							return null;
						}
					}
				}

				connectionPoints[outputIdx] = info.m_object.transform.position;
				break;
			}
			++outputIdx;
		}

		return connectionPoints;
	}

	private bool DoorwayIsOpen(int index)
	{
		DoorwayInfo doorwayInfo = m_doorwayInfos[index];
		if (doorwayInfo.m_blocker != null)
		{
			return false;
		}

		GameObject doorway = doorwayInfo.m_object;
		if (!doorway.activeSelf)
		{
			return true;
		}

		PlatformEffector2D platform = doorway.GetComponent<PlatformEffector2D>();
		if (platform != null && platform.enabled)
		{
			return true;
		}

		return false;
	}
}
