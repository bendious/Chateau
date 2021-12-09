using System;
using UnityEngine;
using UnityEngine.Assertions;


public class RoomController : MonoBehaviour
{
	public GameObject m_roomPrefab;
	public GameObject m_itemPrefab;

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
