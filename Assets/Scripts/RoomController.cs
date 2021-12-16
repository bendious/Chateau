using Platformer.Mechanics;
using System;
using UnityEngine;
using UnityEngine.Assertions;


public class RoomController : MonoBehaviour
{
	public GameObject m_roomPrefab;
	public GameObject m_tablePrefab;
	public GameObject m_enemyPrefab;

	public GameObject m_doorL;
	public GameObject m_doorR;
	public GameObject m_doorB;
	public GameObject m_doorT;

	public Color m_oneWayPlatformColor = new Color(0.3f, 0.2f, 0.1f);

	public float m_roomSpawnPct = 0.5f;
	public int m_spawnDepthMax = 5;

	public int m_tablesMin = 0;
	public int m_tablesMax = 2;


	private bool m_leftOpen = false;
	private bool m_rightOpen = false;
	private bool m_bottomOpen = false;
	private bool m_topOpen = false;


	private void Start()
	{
		// calculate size info
		Bounds bounds = CalculateBounds();
		Vector3 offsetMagH = new Vector3(bounds.size.x, 0.0f, 0.0f);
		Vector3 offsetMagV = new Vector3(0.0f, bounds.size.y, 0.0f);
		Vector3 checkSize = bounds.size - new Vector3(0.1f, 0.1f, 0.0f); // NOTE the small reduction to avoid always collecting ourself

		// replace doors / spawn rooms
		// TODO: randomize order to avoid directional bias?
		MaybeReplaceDoor(ref m_leftOpen, bounds, -offsetMagH, checkSize, m_doorL, roomController => roomController.m_rightOpen = true);
		MaybeReplaceDoor(ref m_rightOpen, bounds, offsetMagH, checkSize, m_doorR, roomController => roomController.m_leftOpen = true);
		MaybeReplaceDoor(ref m_bottomOpen, bounds, -offsetMagV, checkSize, m_doorB, roomController => roomController.m_topOpen = true);
		MaybeReplaceDoor(ref m_topOpen, bounds, offsetMagV, checkSize, m_doorT, roomController => roomController.m_bottomOpen = true);

		// spawn tables
		// TODO: more deliberate spawning
		int tableCount = UnityEngine.Random.Range(m_tablesMin, m_tablesMax + 1);
		float extentX = offsetMagH.x * 0.5f;
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
			Vector3 spawnPos = transform.position + new Vector3(UnityEngine.Random.Range(-extentX + newBounds.extents.x, extentX - newBounds.extents.x), tableExtentY, 0.0f);
			if (Physics2D.OverlapBox(spawnPos + newBounds.center + new Vector3(0.0f, 0.1f, 0.0f), newBounds.size, 0.0f) != null) // NOTE the small offset to avoid collecting the floor; also that this will collect our newly spawned table when at the origin, but that's okay since keeping the start point clear isn't objectionable
			{
				continue; // re-place and try again
			}
			newTable.transform.position = spawnPos;
			newTable = null;
		}
		if (newTable != null)
		{
			Destroy(newTable);
		}

		// spawn enemies
		// TODO: more deliberate spawning
		int enemyCount = UnityEngine.Random.Range(0, 5);
		for (int i = 0; i < enemyCount; ++i)
		{
			const float offsetMagMax = 5.0f; // TODO: calculate from room size
			CapsuleCollider2D enemyCollider = m_enemyPrefab.GetComponent<CapsuleCollider2D>();
			Vector3 spawnCenterPos = transform.position + new Vector3(UnityEngine.Random.Range(-offsetMagMax, offsetMagMax), enemyCollider.size.y - enemyCollider.offset.y, 0.0f);
			GameObject enemyObj = Instantiate(m_enemyPrefab, spawnCenterPos, Quaternion.identity);
			EnemyController enemy = enemyObj.GetComponent<EnemyController>();
			enemy.m_target = Camera.main.GetComponent<Cinemachine.CinemachineBrain>().ActiveVirtualCamera.Follow;
		}
	}


	// see https://gamedev.stackexchange.com/questions/86863/calculating-the-bounding-box-of-a-game-object-based-on-its-children
	private Bounds CalculateBounds()
	{
		Renderer[] renderers = GetComponentsInChildren<Renderer>();
		if (renderers.Length == 0)
		{
			return new Bounds(transform.position, Vector3.zero);
		}
		Bounds b = renderers[0].bounds;
		foreach (Renderer r in renderers)
		{
			b.Encapsulate(r.bounds);
		}
		return b;
	}

	private void MaybeReplaceDoor(ref bool isOpen, Bounds bounds, Vector3 replaceOffset, Vector3 checkSize, GameObject door, Action<RoomController> postReplace)
	{
		bool spawnedFromThisDirection = isOpen;
		bool canSpawnRoom = !spawnedFromThisDirection && m_spawnDepthMax > 0 && Physics2D.OverlapBox(bounds.center + replaceOffset, checkSize, 0.0f) == null;
		Assert.IsTrue(!spawnedFromThisDirection || !canSpawnRoom);
		isOpen = spawnedFromThisDirection || (canSpawnRoom && UnityEngine.Random.value > m_roomSpawnPct);

		if (!isOpen)
		{
			return;
		}

		// enable one-way movement or destroy
		PlatformEffector2D effector = door.GetComponent<PlatformEffector2D>();
		if (effector == null)
		{
			Destroy(door);
		}
		else
		{
			// enable effector for dynamic collisions
			effector.enabled = true;
			door.GetComponent<Collider2D>().usedByEffector = true;

			// set layer for kinematic movement
			door.layer = LayerMask.NameToLayer("OneWayPlatforms");

			// change color for user visibility
			door.GetComponent<SpriteRenderer>().color = m_oneWayPlatformColor;
		}

		if (!canSpawnRoom)
		{
			return;
		}

		RoomController newRoom = Instantiate(m_roomPrefab, transform.position + replaceOffset, Quaternion.identity).GetComponent<RoomController>();
		newRoom.m_roomPrefab = m_roomPrefab; // NOTE that since Unity's method of internal prefab references doesn't allow a script to reference the prefab that contains it, we have to manually update the child's reference here
		newRoom.m_spawnDepthMax = m_spawnDepthMax - 1;
		postReplace(newRoom);
	}
}
