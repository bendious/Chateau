using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;


public class RoomController : MonoBehaviour
{
	public GameObject m_doorPrefab;
	public GameObject m_tablePrefab;

	public GameObject m_doorL;
	public GameObject m_doorR;
	public GameObject m_doorB;
	public GameObject m_doorT;

	public GameObject[] m_ladderPieces;

	public float m_roomSpawnPct = 0.5f;
	public int m_spawnDepthMax = 5;

	public int m_tablesMin = 0;
	public int m_tablesMax = 2;

	public float m_lockedDoorPct = 0.9f;

	public static readonly Color m_oneWayPlatformColor = new(0.3f, 0.2f, 0.1f);


	private bool m_leftConnected = false;
	private bool m_rightConnected = false;
	private bool m_bottomConnected = false;
	private bool m_topConnected = false;

	private /*readonly*/ RoomController m_leftChild;
	private /*readonly*/ RoomController m_rightChild;
	private /*readonly*/ RoomController m_bottomChild;
	private /*readonly*/ RoomController m_topChild;

	private /*readonly*/ Vector2 m_leftDoorPos;
	private /*readonly*/ Vector2 m_rightDoorPos;
	private /*readonly*/ Vector2 m_bottomDoorPos;
	private /*readonly*/ Vector2 m_topDoorPos;

	private /*readonly*/ GameObject m_leftLock;
	private /*readonly*/ GameObject m_rightLock;
	private /*readonly*/ GameObject m_bottomLock;
	private /*readonly*/ GameObject m_topLock;

	private bool m_childrenCreated = false;


	private void Start()
	{
		m_leftDoorPos = m_doorL.transform.position;
		m_rightDoorPos = m_doorR.transform.position;
		m_bottomDoorPos = m_doorB.transform.position;
		m_topDoorPos = m_doorT.transform.position;

		// replace doors / spawn rooms
		// TODO: randomize order to avoid directional bias?
		Bounds bounds = CalculateBounds(false);
		m_leftChild = MaybeReplaceDoor(ref m_leftConnected, Utility.RandomWeighted(GameController.Instance.m_roomPrefabs), bounds, Vector3.left, ref m_leftLock, m_doorL, null, child => child.m_rightConnected = true);
		m_rightChild = MaybeReplaceDoor(ref m_rightConnected, Utility.RandomWeighted(GameController.Instance.m_roomPrefabs), bounds, Vector3.right, ref m_rightLock, m_doorR, null, child => child.m_leftConnected = true);
		m_bottomChild = MaybeReplaceDoor(ref m_bottomConnected, Utility.RandomWeighted(GameController.Instance.m_roomPrefabs), bounds, Vector3.down, ref m_bottomLock, m_doorB, null, child => child.m_topConnected = true);
		m_topChild = MaybeReplaceDoor(ref m_topConnected, Utility.RandomWeighted(GameController.Instance.m_roomPrefabs), bounds, Vector3.up, ref m_topLock, m_doorT, m_ladderPieces, child => child.m_bottomConnected = true);

		m_childrenCreated = true;

		// spawn tables
		// TODO: more deliberate spawning
		int tableCount = UnityEngine.Random.Range(m_tablesMin, m_tablesMax + 1);
		BoxCollider2D tableCollider = m_tablePrefab.GetComponent<BoxCollider2D>();
		float tableExtentY = tableCollider.size.y * 0.5f + tableCollider.edgeRadius - tableCollider.offset.y;
		GameObject newTable = null;
		for (int i = 0; i < tableCount; ++i)
		{
			if (newTable == null)
			{
				newTable = Instantiate(m_tablePrefab); // NOTE that we have to spawn before placement due to size randomization in Awake()
			}
			Bounds newBounds = newTable.GetComponent<Collider2D>().bounds;
			Vector3 spawnPos = new Vector3(bounds.center.x, transform.position.y, transform.position.z) + new Vector3(UnityEngine.Random.Range(-bounds.extents.x + newBounds.extents.x, bounds.extents.x - newBounds.extents.x), tableExtentY, 1.0f);
			if (Physics2D.OverlapBox(spawnPos + newBounds.center + new Vector3(0.0f, 0.1f, 0.0f), newBounds.size, 0.0f) != null) // NOTE the small offset to avoid collecting the floor; also that this will collect our newly spawned table when at the origin, but that's okay since keeping the start point clear isn't objectionable
			{
				continue; // re-place and try again
			}
			newTable.transform.position = spawnPos;
			newTable.GetComponent<TableController>().SpawnItems();
			newTable = null;
		}
		if (newTable != null)
		{
			newTable.SetActive(false); // to prevent being visible for a frame while waiting to despawn
			Simulation.Schedule<ObjectDespawn>().m_object = newTable;
		}
	}


	public bool AllChildrenReady()
	{
		if (!m_childrenCreated)
		{
			return false;
		}
		return (new RoomController[] { m_leftChild, m_rightChild, m_bottomChild, m_topChild }).All(child => child == null || child.AllChildrenReady());
	}

	public Vector3 ChildPosition(bool checkLocks, GameObject targetObj, bool onFloor)
	{
		// enumerate valid options
		RoomController[] options = (new[] { new(this, null), new(m_leftChild, m_leftLock), new(m_rightChild, m_rightLock), new(m_bottomChild, m_bottomLock), Tuple.Create(m_topChild, m_topLock) }).Where(child => child.Item1 != null && child.Item1.m_childrenCreated && (!checkLocks || child.Item2 == null)).Select(pair => pair.Item1).ToArray();

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
			return transform.position + new Vector3(UnityEngine.Random.Range(-xDiffMax, xDiffMax), UnityEngine.Random.Range(0, yMax), 0.0f);
		}

		// return position from child room
		return child.ChildPosition(checkLocks, targetObj, onFloor);
	}

	public List<Vector2> ChildRoomPath(Vector2 startPosition, Vector2 endPositionPreoffset, Vector2 offsetMag)
	{
		// TODO: efficiency?
		// find root-->start and root-->end paths
		List<RoomController> startPath = RoomPathFromRoot(startPosition, new());
		if (startPath == null)
		{
			return null; // TODO: find closest reachable point?
		}
		List<RoomController> endPath = RoomPathFromRoot(endPositionPreoffset, new());
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

		// convert rooms to waypoints
		List<Vector2> waypointPath = new();
		for (int i = 0, n = startPath.Count - 1; i < n; ++i)
		{
			Vector2 connectionPos = RoomConnection(startPath[i], startPath[i + 1]);
			Assert.IsFalse(connectionPos == Vector2.zero);
			waypointPath.Add(connectionPos);
		}

		// add valid end point
		float semifinalX = waypointPath.Count > 0 ? waypointPath.Last().x : startPosition.x;
		Vector2 endPos = endPositionPreoffset + (semifinalX >= endPositionPreoffset.x ? offsetMag : new Vector2(-offsetMag.x, offsetMag.y));
		Bounds endRoomBounds = startPath.Last().CalculateBounds(true);
		waypointPath.Add(endRoomBounds.Contains(new(endPos.x, endPos.y, endRoomBounds.center.z)) ? endPos : endRoomBounds.ClosestPoint(endPos)); // TODO: flip offset if closest interior point is significantly different from endPos?

		return waypointPath;
	}

	public Tuple<List<RoomController>, int> RoomPathLongest(int startDistance = 0)
	{
		// enumerate non-null children
		Assert.IsTrue(m_childrenCreated);
		Tuple<RoomController, int>[] childrenPreprocess = (new[] { new(m_leftChild, m_leftLock != null ? 1 : 0), new(m_rightChild, m_rightLock != null ? 1 : 0), new(m_bottomChild, m_bottomLock != null ? 1 : 0), Tuple.Create(m_topChild, m_topLock != null ? 1 : 0) }).Where(child => child.Item1 != null).ToArray();
		if (childrenPreprocess.Length == 0)
		{
			return new(new List<RoomController> { this }, startDistance);
		}

		// recursively find farthest distance
		Tuple<List<RoomController>, int>[] childrenPostprocessed = childrenPreprocess.Select(child => child.Item1.RoomPathLongest(child.Item2)).ToArray();
		int maxDistance = childrenPostprocessed.Max(a => a.Item2);

		// pick random child at farthest distance
		Tuple<List<RoomController>, int>[] childrenMax = childrenPostprocessed.Where(childTuple => childTuple.Item2 >= maxDistance).ToArray();
		List<RoomController> pathFinal = childrenMax[UnityEngine.Random.Range(0, childrenMax.Length)].Item1;
		pathFinal.Insert(0, this);
		return new(pathFinal, maxDistance + 1);
	}

	public bool SpawnChildRoom(GameObject roomPrefab)
	{
		Bounds bounds = CalculateBounds(false);

		int spawnDepthOrig = m_spawnDepthMax;
		m_spawnDepthMax = Math.Max(m_spawnDepthMax, 1);

		// TODO: randomize order
		if (m_leftChild == null && m_doorL != null)
		{
			m_leftChild = MaybeReplaceDoor(ref m_leftConnected, roomPrefab, bounds, Vector3.left, ref m_leftLock, m_doorL, null, child => child.m_rightConnected = true);
			if (m_leftChild != null)
			{
				m_spawnDepthMax = spawnDepthOrig;
				return true;
			}
		}

		if (m_rightChild == null && m_doorR != null)
		{
			m_rightChild = MaybeReplaceDoor(ref m_rightConnected, roomPrefab, bounds, Vector3.right, ref m_rightLock, m_doorR, null, child => child.m_leftConnected = true);
			if (m_rightChild != null)
			{
				m_spawnDepthMax = spawnDepthOrig;
				return true;
			}
		}

		if (m_bottomChild == null && m_doorB != null)
		{
			m_bottomChild = MaybeReplaceDoor(ref m_bottomConnected, roomPrefab, bounds, Vector3.down, ref m_bottomLock, m_doorB, null, child => child.m_topConnected = true);
			if (m_bottomChild != null)
			{
				m_spawnDepthMax = spawnDepthOrig;
				return true;
			}
		}

		if (m_topChild == null && m_doorT != null)
		{
			m_topChild = MaybeReplaceDoor(ref m_topConnected, roomPrefab, bounds, Vector3.up, ref m_topLock, m_doorT, m_ladderPieces, child => child.m_bottomConnected = true);
			if (m_topChild != null)
			{
				foreach (GameObject piece in m_ladderPieces)
				{
					piece.SetActive(true);
				}
				m_spawnDepthMax = spawnDepthOrig;
				return true;
			}
		}

		m_spawnDepthMax = spawnDepthOrig;
		return false;
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

	private RoomController MaybeReplaceDoor(ref bool isOpen, GameObject roomPrefab, Bounds bounds, Vector3 replaceDirection, ref GameObject lockObj, GameObject door, GameObject[] ladderPieces, Action<RoomController> postReplace)
	{
		Assert.AreApproximatelyEqual(replaceDirection.magnitude, 1.0f);
		bool spawnedFromThisDirection = isOpen;

		Bounds childBounds = roomPrefab.GetComponent<RoomController>().CalculateBounds(false);
		Vector3 pivotToCenter = bounds.center - transform.position;
		Vector3 childPivotToCenter = childBounds.center - roomPrefab.transform.position;
		Vector3 childPivotPos = transform.position + Vector3.Scale(replaceDirection, bounds.extents + childBounds.extents + (Vector2.Dot(pivotToCenter, replaceDirection) >= 0.0f ? pivotToCenter : -pivotToCenter) + (Vector2.Dot(childPivotToCenter, replaceDirection) >= 0.0f ? -childPivotToCenter : childPivotToCenter));

		bool canSpawnRoom = !spawnedFromThisDirection && m_spawnDepthMax > 0 && Physics2D.OverlapBox(childPivotPos + childPivotToCenter, childBounds.size - new Vector3(0.1f, 0.1f, 0.0f), 0.0f) == null; // NOTE the small size reduction to avoid always collecting ourself
		isOpen = spawnedFromThisDirection || (canSpawnRoom && UnityEngine.Random.value < m_roomSpawnPct);

		if (!isOpen)
		{
			if (ladderPieces != null)
			{
				foreach (GameObject piece in ladderPieces)
				{
					piece.SetActive(false);
				}
			}

			return null;
		}

		if (!spawnedFromThisDirection && UnityEngine.Random.value <= m_lockedDoorPct)
		{
			// create locked door
			lockObj = Instantiate(m_doorPrefab, door.transform.position + Vector3.back, Quaternion.identity); // NOTE the depth decrease to ensure rendering on top of platforms
			Vector2 size = door.GetComponent<BoxCollider2D>().size * door.transform.localScale;
			lockObj.GetComponent<BoxCollider2D>().size = size;
			lockObj.GetComponent<SpriteRenderer>().size = size;
			// TODO: update shadow caster shape once it is programmatically accessible
		}

		// enable one-way movement or destroy
		PlatformEffector2D effector = door.GetComponent<PlatformEffector2D>();
		if (effector == null)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = door;
		}
		else
		{
			// enable effector for dynamic collisions
			effector.enabled = true;
			door.GetComponent<Collider2D>().usedByEffector = true;

			// set layer for kinematic movement
			door.layer = LayerMask.NameToLayer("OneWayPlatforms");

			// change color/shadows for user visibility
			door.GetComponent<SpriteRenderer>().color = m_oneWayPlatformColor;
			Destroy(door.GetComponent<UnityEngine.Rendering.Universal.ShadowCaster2D>());
		}

		if (!canSpawnRoom)
		{
			return null;
		}

		RoomController newRoom = Instantiate(roomPrefab, childPivotPos, Quaternion.identity).GetComponent<RoomController>();
		newRoom.m_spawnDepthMax = m_spawnDepthMax - 1;
		postReplace(newRoom);
		return newRoom;
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
		Assert.IsTrue(m_childrenCreated);
		RoomController[] children = (new RoomController[] { m_leftChild, m_rightChild, m_bottomChild, m_topChild }).Where(child => child != null).ToArray();
		foreach (RoomController child in children)
		{
			List<RoomController> childPath = child.RoomPathFromRoot(endPosition, prePath);
			if (childPath != null)
			{
				return childPath;
			}
		}

		return null;
	}

	private Vector2 RoomConnection(RoomController a, RoomController b)
	{
		// TODO: efficiency?
		if (a.m_leftChild == b)
		{
			return a.m_leftDoorPos;
		}
		if (a.m_rightChild == b)
		{
			return a.m_rightDoorPos;
		}
		if (a.m_bottomChild == b)
		{
			return a.m_bottomDoorPos;
		}
		if (a.m_topChild == b)
		{
			return a.m_topDoorPos;
		}
		if (b.m_leftChild == a)
		{
			return b.m_leftDoorPos;
		}
		if (b.m_rightChild == a)
		{
			return b.m_rightDoorPos;
		}
		if (b.m_bottomChild == a)
		{
			return b.m_bottomDoorPos;
		}
		if (b.m_topChild == a)
		{
			return b.m_topDoorPos;
		}

		return Vector3.zero; // TODO: better no-connection return value?
	}
}
