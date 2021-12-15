using Platformer.Mechanics;
using System;
using UnityEngine;
using UnityEngine.Assertions;


public class RoomController : MonoBehaviour
{
	public GameObject m_roomPrefab;
	public GameObject m_itemPrefab;
	public GameObject m_enemyPrefab;

	public GameObject m_doorL;
	public GameObject m_doorR;
	public GameObject m_doorB;
	public GameObject m_doorT;

	public float m_roomSpawnPct = 0.5f;
	public int m_spawnDepthMax = 5;


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

		// spawn items
		// TODO: more deliberate spawning
		int itemCount = UnityEngine.Random.Range(0, 5);
		for (int i = 0; i < itemCount; ++i)
		{
			const float offsetMagMax = 5.0f; // TODO: calculate from room size
			BoxCollider2D itemCollider = m_itemPrefab.GetComponent<BoxCollider2D>();
			Vector3 spawnCenterPos = transform.position + new Vector3(UnityEngine.Random.Range(-offsetMagMax, offsetMagMax), + itemCollider.size.y * 0.5f + itemCollider.edgeRadius, 0.0f);
			Instantiate(m_itemPrefab, spawnCenterPos, Quaternion.identity);
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
			door.GetComponent<BoxCollider2D>().enabled = true; // TODO: why are instantiated door colliders sometimes disabled?
			return;
		}

		Destroy(door);

		if (!canSpawnRoom)
		{
			return;
		}

		GameObject newRoomObj = Instantiate(m_roomPrefab, transform.position + replaceOffset, Quaternion.identity);
		RoomController newRoom = newRoomObj.GetComponent<RoomController>();
		newRoom.m_spawnDepthMax = m_spawnDepthMax - 1;
		postReplace(newRoom);
	}
}
