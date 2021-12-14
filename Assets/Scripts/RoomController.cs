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

	public float m_roomSpawnPct = 0.5f;


	private bool m_leftOpen = false;
	private bool m_rightOpen = false;


	private void Start()
	{
		// TODO: check whether there is room to spawn rooms
		bool fromLeft = m_leftOpen;
		bool fromRight = m_rightOpen;
		Assert.IsTrue(!fromLeft || !fromRight);
		m_leftOpen = fromLeft || UnityEngine.Random.value > m_roomSpawnPct;
		m_rightOpen = fromRight || UnityEngine.Random.value > m_roomSpawnPct;

		Vector3 offsetMag = new Vector3(12.0f, 0.0f, 0.0f); // TODO: calculate from room size
		MaybeReplaceDoor(m_leftOpen, m_doorL, !fromLeft, -offsetMag, roomController => roomController.m_rightOpen = true);
		MaybeReplaceDoor(m_rightOpen, m_doorR, !fromRight, offsetMag, roomController => roomController.m_leftOpen = true);

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


	private void MaybeReplaceDoor(bool remove, GameObject door, bool replace, Vector3 replaceOffset, Action<RoomController> postReplace)
	{
		if (!remove)
		{
			door.GetComponent<BoxCollider2D>().enabled = true; // TODO: why are instantiated door colliders sometimes disabled?
			return;
		}

		Destroy(door);

		if (!replace)
		{
			return;
		}

		GameObject newRoom = Instantiate(m_roomPrefab, transform.position + replaceOffset, Quaternion.identity);
		postReplace(newRoom.GetComponent<RoomController>());
	}
}
