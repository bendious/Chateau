using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;


public class CreditsController : MonoBehaviour
{
	[SerializeField] private float m_ctBackdropRadius = 1.0f;

	[SerializeField] private float m_npcSpawnSecondsMin = 10.0f;
	[SerializeField] private float m_npcSpawnSecondsMax = 60.0f;


	private float m_cameraSize;
	private RoomController m_roomCurrent;

	private bool m_npcSpawningInProgress = false;
	private int m_npcIndexLastSpawned = -1;
	private readonly System.Collections.Generic.List<GameObject> m_npcsOrdered = new();


	private void Start()
	{
		m_cameraSize = GameController.Instance.m_vCamMain.GetCinemachineComponent<Cinemachine.CinemachineFramingTransposer>().m_MinimumOrthoSize;
		StartCoroutine(SetInitialRoomDelayed());

		ObjectDespawn.OnExecute += OnObjectDespawn; // NOTE that since this is just to start a coroutine, it might make more sense to hook/unhook it in On{Enable/Disable}(), but OnEnable() didn't seem to be getting called in standalone builds for some reason...
		StartCoroutine(SpawnNpcsDelayed());
	}

	private void OnDestroy()
	{
		ObjectDespawn.OnExecute -= OnObjectDespawn;
	}


	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnForward(InputValue input)
	{
		UpdateRoom(room => room.FirstChild);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnBack(InputValue input)
	{
		UpdateRoom(room => room.Parent);
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "defined by InputSystem / PlayerInput component")]
	public void OnPause(InputValue input) => GameController.Instance.TogglePause(true);


	private IEnumerator SetInitialRoomDelayed()
	{
		yield return new WaitForEndOfFrame(); // due to GameController.m_startRoom not being immediately available
		UpdateRoom(null);
	}

	private void UpdateRoom(System.Func<RoomController, RoomController> roomToNext)
	{
		if (m_roomCurrent != null)
		{
			GameController.Instance.RemoveCameraTargets(m_roomCurrent.transform, m_roomCurrent.m_backdrop.transform);
		}

		RoomController roomNext = roomToNext == null ? null : roomToNext(m_roomCurrent);

		m_roomCurrent = roomNext != null ? roomNext : GameController.Instance.RoomFromPosition(Vector2.zero);

		GameController.Instance.AddCameraTargetsSized(m_ctBackdropRadius, m_roomCurrent.m_backdrop.transform);
		if (m_roomCurrent.Bounds.extents.y > m_cameraSize)
		{
			GameController.Instance.AddCameraTargets(m_roomCurrent.transform);
		}
	}

	private void OnObjectDespawn(ObjectDespawn evt)
	{
		if (!m_npcSpawningInProgress && m_npcsOrdered.Contains(evt.m_object))
		{
			StartCoroutine(SpawnNpcsDelayed());
		}
	}

	private IEnumerator SpawnNpcsDelayed()
	{
		m_npcSpawningInProgress = true;

		// due to GameController.m_npcs[] not being assigned until Start() and GameController.m_npcsInstantiated[] not necessarily being updated before us when an NPC despawns, we wait before checking NPC counts
		yield return new WaitForEndOfFrame();

		int npcsTotal = GameController.Instance.NpcsTotal;
		while (GameController.Instance.NpcsInstantiatedCount < npcsTotal)
		{
			yield return new WaitForSeconds(Random.Range(m_npcSpawnSecondsMin, m_npcSpawnSecondsMax));

			// increment index, skipping existing NPCs
			for (int i = 0; i < npcsTotal; ++i)
			{
				m_npcIndexLastSpawned = (m_npcIndexLastSpawned + 1) % npcsTotal;
				if (m_npcIndexLastSpawned >= m_npcsOrdered.Count)
				{
					m_npcsOrdered.Add(null);
				}
				if (m_npcsOrdered[m_npcIndexLastSpawned] == null)
				{
					break;
				}
			}
			Debug.Assert(m_npcsOrdered[m_npcIndexLastSpawned] == null);

			// instantiate & assign
			GameObject npcObj = Instantiate(GameController.Instance.m_npcPrefabs.RandomWeighted().gameObject);
			npcObj.GetComponent<InteractNpc>().Index = m_npcIndexLastSpawned;
			m_npcsOrdered[m_npcIndexLastSpawned] = npcObj;

			// hand-off to GameController
			AIController ai = npcObj.GetComponent<AIController>();
			GameController.Instance.NpcAdd(ai);
			GameController.Instance.EnterAtDoor(ai);
		}

		m_npcSpawningInProgress = false;
	}
}
