using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


public class RoomController : MonoBehaviour
{
	public WeightedObject<GameObject>[] m_doorPrefabs;
	public WeightedObject<GameObject>[] m_doorSecretPrefabs;
	public GameObject m_tablePrefab;

	public GameObject[] m_doorways;

	public GameObject[] m_ladderPieces;

	public static readonly Color m_oneWayPlatformColor = new(0.3f, 0.2f, 0.1f);


	private struct DoorwayInfo
	{
		public Vector2 m_position;
		public RoomController m_parentRoom;
		public RoomController m_childRoom; // TODO: make bidirectional?
		public GameObject m_blocker;
	}
	private /*readonly*/ DoorwayInfo[] m_doorwayInfos;

	private /*readonly*/ LayoutGenerator.Node m_layoutNode;


	private void Awake()
	{
		m_doorwayInfos = new DoorwayInfo[m_doorways.Length];
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (m_layoutNode != null && ConsoleCommands.LayoutDebug)
		{
			Vector3 centerPos = CalculateBounds(false).center; // TODO: efficiency?
			UnityEditor.Handles.Label(centerPos, m_layoutNode.m_type.ToString());
			if (m_layoutNode.DirectParents != null)
			{
				foreach (LayoutGenerator.Node node in m_layoutNode.DirectParents)
				{
					UnityEditor.Handles.DrawLine(centerPos, node.m_room.transform.position);
				}
				using (new UnityEditor.Handles.DrawingScope(Color.red))
				{
					UnityEditor.Handles.DrawLine(centerPos, m_layoutNode.TightCoupleParent.m_room.transform.position);
				}
			}
		}
	}
#endif


	public void Initialize(LayoutGenerator.Node layoutNode)
	{
		Assert.IsNull(m_layoutNode);
		m_layoutNode = layoutNode;
		Debug.Assert(layoutNode.m_room == null);
		layoutNode.m_room = this;

		// set up doorways
		for (int doorwayIdx = 0; doorwayIdx < m_doorways.Length; ++doorwayIdx)
		{
			GameObject doorway = m_doorways[doorwayIdx];

			// record doorway position in case the object is removed
			m_doorwayInfos[doorwayIdx].m_position = doorway.transform.position;

			if (m_doorwayInfos[doorwayIdx].m_parentRoom != null)
			{
				// open doorway to parent
				OpenDoorway(doorway, DoorwayDirection(doorway).y > 0.0f);
			}
		}

		// spawn contents
		switch (m_layoutNode.m_type)
		{
			case LayoutGenerator.Node.Type.Items:
				// spawn table
				Bounds bounds = CalculateBounds(false);
				BoxCollider2D tableCollider = m_tablePrefab.GetComponent<BoxCollider2D>();
				float tableExtentY = tableCollider.size.y * 0.5f + tableCollider.edgeRadius - tableCollider.offset.y;
				GameObject newTable = Instantiate(m_tablePrefab, transform); // NOTE that we have to spawn before placement due to size randomization in Awake() // TODO: guarantee size will fit in available space?
				for (int failsafeCount = 0; failsafeCount < 100; ++failsafeCount)
				{
					Bounds newBounds = newTable.GetComponent<Collider2D>().bounds;
					Vector3 spawnPos = new Vector3(bounds.center.x, transform.position.y, transform.position.z) + new Vector3(UnityEngine.Random.Range(-bounds.extents.x + newBounds.extents.x, bounds.extents.x - newBounds.extents.x), tableExtentY, 1.0f);
					if (Physics2D.OverlapBox(spawnPos + newBounds.center + new Vector3(0.0f, 0.1f, 0.0f), newBounds.size, 0.0f) != null) // NOTE the small offset to avoid collecting the floor; also that this would collect our newly spawned table when at the origin, but that shouldn't happen and would be okay anyway since keeping the start point clear isn't objectionable
					{
						continue; // re-place and try again
					}
					newTable.transform.position = spawnPos;
					newTable.GetComponent<TableController>().SpawnItems();
					newTable = null;
					break;
				}
				if (newTable != null)
				{
					newTable.SetActive(false); // to prevent being visible for a frame while waiting to despawn
					Simulation.Schedule<ObjectDespawn>().m_object = newTable;
				}
				break;

			default:
				break;
		}
	}

	public Vector3 ChildPosition(bool checkLocks, GameObject targetObj, bool onFloor, bool recursive)
	{
		// enumerate valid options
		RoomController[] options = recursive ? m_doorwayInfos.Where(info => info.m_childRoom != null && (!checkLocks || info.m_blocker == null)).Select(pair => pair.m_childRoom).ToArray() : new RoomController[] { this };

		// weight options based on distance to target
		float[] optionWeights = targetObj == null ? Enumerable.Repeat(1.0f, options.Length).ToArray() : options.Select(option => 1.0f / Vector3.Distance(option.transform.position, targetObj.transform.position)).ToArray();
		RoomController child = options.Length == 0 ? this : Utility.RandomWeighted(options, optionWeights);

		if (child == this)
		{
			// return interior position
			// TODO: avoid spawning right on top of targetObj
			Bounds bounds = CalculateBounds(true);
			float xDiffMax = bounds.extents.x;
			float yMax = onFloor ? 0.0f : bounds.size.y;
			return new Vector3(bounds.center.x + UnityEngine.Random.Range(-xDiffMax, xDiffMax), transform.position.y + UnityEngine.Random.Range(0, yMax), transform.position.z); // NOTE the assumptions that the object position is on the floor of the room but not necessarily centered
		}

		// return position from child room
		return child.ChildPosition(checkLocks, targetObj, onFloor, recursive);
	}

	public List<RoomController> ChildRoomPath(Vector2 startPosition, Vector2 endPosition)
	{
		// TODO: efficiency?
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

		return startPath;
	}

	public List<Vector2> ChildPositionPath(Vector2 startPosition, Vector2 endPositionPreoffset, Vector2 offsetMag)
	{
		List<RoomController> roomPath = ChildRoomPath(startPosition, endPositionPreoffset);
		if (roomPath == null)
		{
			return null;
		}

		// convert rooms to waypoints
		List<Vector2> waypointPath = new();
		for (int i = 0, n = roomPath.Count - 1; i < n; ++i)
		{
			Vector2[] connectionPoints = RoomConnection(roomPath[i], roomPath[i + 1]);
			Assert.IsTrue(connectionPoints.Length > 0 && !connectionPoints.Contains(Vector2.zero));
			waypointPath.AddRange(connectionPoints);
		}

		// add valid end point
		float semifinalX = waypointPath.Count > 0 ? waypointPath.Last().x : startPosition.x;
		Vector2 endPos = endPositionPreoffset + (semifinalX >= endPositionPreoffset.x ? offsetMag : new Vector2(-offsetMag.x, offsetMag.y));
		Bounds endRoomBounds = roomPath.Last().CalculateBounds(true);
		waypointPath.Add(endRoomBounds.Contains(new(endPos.x, endPos.y, endRoomBounds.center.z)) ? endPos : endRoomBounds.ClosestPoint(endPos)); // TODO: flip offset if closest interior point is significantly different from endPos?

		return waypointPath;
	}

	public bool SpawnChildRoom(GameObject roomPrefab, LayoutGenerator.Node layoutNode)
	{
		Debug.Assert(layoutNode.m_room == null);

		Bounds bounds = CalculateBounds(false);

		bool success = DoorwaysRandomOrder(i =>
		{
			if (m_doorwayInfos[i].m_childRoom != null || m_doorwayInfos[i].m_parentRoom != null)
			{
				return false;
			}

			// maybe replace/remove
			GameObject doorway = m_doorways[i];
			MaybeReplaceDoor(ref m_doorwayInfos[i], roomPrefab, bounds, DoorwayDirection(doorway), doorway, layoutNode);
			return m_doorwayInfos[i].m_childRoom != null;
		});

		if (!success)
		{
			// try spawning from children
			success = DoorwaysRandomOrder(i =>
			{
				DoorwayInfo doorway = m_doorwayInfos[i];
				if (doorway.m_childRoom == null)
				{
					return false;
				}
				return doorway.m_childRoom.SpawnChildRoom(roomPrefab, layoutNode);
			});
		}

		return success;
	}


	private Vector3 DoorwayDirection(GameObject doorway)
	{
		Vector3 doorwaySize = doorway.GetComponent<Collider2D>().bounds.size;
		bool isTrapdoor = doorwaySize.x > doorwaySize.y;
		Vector3 pivotToDoorway = doorway.transform.position - transform.position;
		return isTrapdoor ? new Vector3(0.0f, Mathf.Sign(pivotToDoorway.y), 0.0f) : new Vector3(Mathf.Sign(pivotToDoorway.x), 0.0f, 0.0f);
	}

	// see https://gamedev.stackexchange.com/questions/86863/calculating-the-bounding-box-of-a-game-object-based-on-its-children
	private Bounds CalculateBounds(bool interiorOnly)
	{
		Renderer[] renderers = GetComponentsInChildren<Renderer>();
		if (renderers.Length == 0)
		{
			return new(transform.position, Vector3.zero);
		}
		Bounds b = renderers[0].bounds;
		foreach (Renderer r in renderers)
		{
			b.Encapsulate(r.bounds);
		}
		if (interiorOnly)
		{
			b.Expand(new Vector3(-1.0f, -1.0f, 0.0f)); // TODO: dynamically determine wall/floor thickness
		}
		return b;
	}

	private bool DoorwaysRandomOrder(Func<int, bool> f)
	{
		int[] order = Enumerable.Range(0, m_doorwayInfos.Length).OrderBy(i => UnityEngine.Random.value).ToArray();
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

	// TODO: inline?
	private void MaybeReplaceDoor(ref DoorwayInfo doorwayInfo, GameObject roomPrefab, Bounds bounds, Vector3 replaceDirection, GameObject doorway, LayoutGenerator.Node childNode)
	{
		Assert.IsNull(doorwayInfo.m_parentRoom);
		Assert.IsNull(doorwayInfo.m_childRoom);
		Assert.AreApproximatelyEqual(replaceDirection.magnitude, 1.0f);

		Bounds childBounds = roomPrefab.GetComponent<RoomController>().CalculateBounds(false);
		Vector3 pivotToCenter = bounds.center - transform.position;
		Vector3 childPivotToCenter = childBounds.center - roomPrefab.transform.position;
		Vector3 childPivotPos = transform.position + Vector3.Scale(replaceDirection, bounds.extents + childBounds.extents + (Vector2.Dot(pivotToCenter, replaceDirection) >= 0.0f ? pivotToCenter : -pivotToCenter) + (Vector2.Dot(childPivotToCenter, replaceDirection) >= 0.0f ? -childPivotToCenter : childPivotToCenter));

		bool isOpen = Physics2D.OverlapBox(childPivotPos + childPivotToCenter, childBounds.size - new Vector3(0.1f, 0.1f, 0.0f), 0.0f) == null; // NOTE the small size reduction to avoid always collecting ourself
		if (!isOpen)
		{
			return;
		}

		OpenDoorway(doorway, replaceDirection.y > 0.0f);

		RoomController childRoom = Instantiate(roomPrefab, childPivotPos, Quaternion.identity).GetComponent<RoomController>();
		doorwayInfo.m_childRoom = childRoom;

		// set child's parent connection
		int returnIdx = childRoom.m_doorways.Zip(childRoom.m_doorwayInfos, (newDoorway, info) =>
		{
			Vector3 doorwaySize = newDoorway.GetComponent<Collider2D>().bounds.size;
			bool isTrapdoor = doorwaySize.x > doorwaySize.y;
			return Vector2.Dot((Vector2)childRoom.transform.position - (Vector2)newDoorway.transform.position, replaceDirection) > 0.0f && isTrapdoor == (Mathf.Abs(replaceDirection.x) < Mathf.Abs(replaceDirection.y)); // TODO: better way of determining reverse direction doorway?
		}).ToList().IndexOf(true);

		doorwayInfo.m_childRoom.m_doorwayInfos[returnIdx].m_parentRoom = this;

		bool isLock = m_layoutNode.m_type == LayoutGenerator.Node.Type.Lock;
		if ((isLock || m_layoutNode.m_type == LayoutGenerator.Node.Type.Secret) && childNode.DirectParents.Exists(node => node == m_layoutNode))
		{
			// create gate
			Assert.IsNull(doorwayInfo.m_blocker);
			doorwayInfo.m_blocker = Instantiate(Utility.RandomWeighted(isLock ? m_doorPrefabs : m_doorSecretPrefabs), doorway.transform.position + Vector3.back, Quaternion.identity, transform); // NOTE the depth decrease to ensure rendering on top of platforms
			doorwayInfo.m_childRoom.m_doorwayInfos[returnIdx].m_blocker = doorwayInfo.m_blocker;

			// resize gate to fit doorway
			Vector2 size = doorway.GetComponent<BoxCollider2D>().size * doorway.transform.localScale;
			doorwayInfo.m_blocker.GetComponent<BoxCollider2D>().size = size;
			doorwayInfo.m_blocker.GetComponent<SpriteRenderer>().size = size;
			// TODO: update shadow caster shape once it is programmatically accessible

			// spawn key(s)
			if (isLock)
			{
				doorwayInfo.m_blocker.GetComponent<IUnlockable>().SpawnKeys(this, m_layoutNode.DirectParents.Where(node => node.m_type == LayoutGenerator.Node.Type.Key).Select(node => node.m_room).ToArray());
			}
		}

		childRoom.Initialize(childNode);
	}

	private void OpenDoorway(GameObject doorway, bool upward)
	{
		// TODO: spawn ladders rather than embedding in prefabs, handle multiple upward doorways?
		if (upward && m_ladderPieces != null)
		{
			foreach (GameObject piece in m_ladderPieces)
			{
				piece.SetActive(true);
			}
		}

		// enable one-way movement or destroy
		PlatformEffector2D effector = doorway.GetComponent<PlatformEffector2D>();
		if (effector == null)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = doorway;
		}
		else
		{
			// enable effector for dynamic collisions
			effector.enabled = true;
			doorway.GetComponent<Collider2D>().usedByEffector = true;

			// set layer for kinematic movement
			doorway.layer = LayerMask.NameToLayer("OneWayPlatforms");

			// change color/shadows for user visibility
			doorway.GetComponent<SpriteRenderer>().color = m_oneWayPlatformColor;
			Destroy(doorway.GetComponent<UnityEngine.Rendering.Universal.ShadowCaster2D>());
		}
	}

	private List<RoomController> RoomPathFromRoot(Vector2 endPosition, List<RoomController> prePath)
	{
		// TODO: A* algorithm?

		prePath = new(prePath); // NOTE the copy to prevent storing up entries from other branches of the recursion
		prePath.Add(this);

		Bounds bounds = CalculateBounds(false);
		Vector3 pos3D = new(endPosition.x, endPosition.y, bounds.center.z);
		if (bounds.Contains(pos3D))
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

	private Vector2[] RoomConnection(RoomController from, RoomController to)
	{
		// TODO: efficiency?
		Vector2[] connectionPoints = new Vector2[2];
		for (int i = 0; i < m_doorwayInfos.Length; ++i)
		{
			DoorwayInfo infoFrom = from.m_doorwayInfos[i];
			if (infoFrom.m_childRoom == to || infoFrom.m_parentRoom == to)
			{
				connectionPoints[0] = infoFrom.m_position;
			}
			DoorwayInfo infoTo = to.m_doorwayInfos[i];
			if (infoTo.m_childRoom == from || infoTo.m_parentRoom == from)
			{
				connectionPoints[1] = infoTo.m_position;
			}
		}

		return connectionPoints;
	}
}
