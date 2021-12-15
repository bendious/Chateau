using Platformer.Core;
using UnityEngine;


public class GameController : MonoBehaviour
{
	public GameObject m_roomPrefab;


	private void Start()
	{
		Instantiate(m_roomPrefab).GetComponent<RoomController>().m_roomPrefab = m_roomPrefab; // NOTE that since Unity's method of internal prefab references doesn't allow a script to reference the prefab that contains it, we have to manually update the child's reference here
	}

	private void Update()
	{
		Simulation.Tick();
	}
}
