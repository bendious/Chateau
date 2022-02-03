using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


public class BossRoom : MonoBehaviour
{
	public EnemyController m_boss;

	public GameObject m_gatePrefab;


	private Vector3 m_bossStartPos;

	private struct DoorInfo
	{
		public GameObject m_object;
		public Bounds m_bounds;
	};
	private DoorInfo[] m_doors;

	private readonly List<GameObject> m_spawnedGates = new();


	private void Awake()
	{
		m_boss.transform.SetParent(null); // necessary since all prefab contents have to start as children of the main object
		m_bossStartPos = m_boss.transform.position;

		// add event handlers
#if DEBUG
		GameOver.OnExecute += DebugOnGameOver;
#endif
		// TODO: reset camera zoom upon respawn
		ObjectDespawn.OnExecute += OnObjectDespawn;

		// track doorways
		// TODO: generalize?
		RoomController roomScript = GetComponent<RoomController>();
		m_doors = new DoorInfo[4];
		m_doors[0] = new DoorInfo { m_object = roomScript.m_doorL, m_bounds = roomScript.m_doorL.GetComponent<Collider2D>().bounds };
		m_doors[1] = new DoorInfo { m_object = roomScript.m_doorR, m_bounds = roomScript.m_doorR.GetComponent<Collider2D>().bounds };
		m_doors[2] = new DoorInfo { m_object = roomScript.m_doorB, m_bounds = roomScript.m_doorB.GetComponent<Collider2D>().bounds };
		m_doors[3] = new DoorInfo { m_object = roomScript.m_doorT, m_bounds = roomScript.m_doorT.GetComponent<Collider2D>().bounds };
	}

	private void OnTriggerEnter2D(Collider2D collision)
	{
		if (collision.gameObject != GameController.Instance.m_avatar.gameObject)
		{
			return;
		}
		GetComponent<Collider2D>().enabled = false;

		// seal room
		// TODO: inform RoomController of gates for pathfinding correctness?
		foreach (DoorInfo info in m_doors)
		{
			if (info.m_object != null)
			{
				continue;
			}
			GameObject newGate = Instantiate(m_gatePrefab, info.m_bounds.center, Quaternion.identity, transform);
			m_spawnedGates.Add(newGate);
			newGate.GetComponent<BoxCollider2D>().size = info.m_bounds.size;
			newGate.GetComponent<SpriteRenderer>().size = info.m_bounds.size;
		}

		m_boss.m_target = collision.gameObject.transform;

		StartCoroutine(UpdateIntro());
	}

	private void OnDestroy()
	{
#if DEBUG
		GameOver.OnExecute -= DebugOnGameOver;
#endif
		ObjectDespawn.OnExecute -= OnObjectDespawn;
	}


#if DEBUG
	private void DebugOnGameOver(GameOver evt)
	{
		// reset room entrance(s)
		foreach (GameObject gate in m_spawnedGates)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = gate;
		}
		m_spawnedGates.Clear();

		// reset boss
		if (m_boss.enabled)
		{
			m_boss.enabled = false;
			m_boss.GetComponent<Health>().m_invincible = true;
			m_boss.Teleport(m_bossStartPos); // TODO: set goal and navigate rather than snapping
		}

		// re-enable trigger
		GetComponent<Collider2D>().enabled = true;
	}
#endif

	private void OnObjectDespawn(ObjectDespawn evt)
	{
		if (m_boss != null && evt.m_object.gameObject == m_boss.gameObject)
		{
			// TODO: start zoom-in?

			GameController.Instance.OnVictory();
		}
	}

	private IEnumerator UpdateIntro()
	{
		const float smoothTimeSlow = 0.5f;
		const float smoothTimeFast = 0.25f;

		// TODO: SFX/music

		// pause
		yield return new WaitForSeconds(1.0f);

		// recolor
		SpriteRenderer[] bossRenderers = m_boss.GetComponentsInChildren<SpriteRenderer>().Where(renderer => renderer.GetComponent<ItemController>() == null).ToArray();
		Light2D bossLight = m_boss.GetComponent<Light2D>();
		const float intensityTarget = 1.0f;
		Vector4 colorSpeedCur = Vector4.zero;
		float lightSpeedCur = 0.0f;
		while (!Utility.ColorsSimilar(bossRenderers.First().color, Color.red) || !Utility.FloatEqual(bossLight.intensity, intensityTarget)) // TODO: don't assume colors are always in sync?
		{
			foreach (SpriteRenderer bossRenderer in bossRenderers)
			{
				bossRenderer.color = Utility.SmoothDamp(bossRenderer.color, Color.red, ref colorSpeedCur, smoothTimeSlow);
			}
			bossLight.intensity = Mathf.SmoothDamp(bossLight.intensity, intensityTarget, ref lightSpeedCur, smoothTimeSlow);
			yield return null;
		}

		// float into air
		float yTarget = m_bossStartPos.y + 3.0f;
		float ySpeedCur = 0.0f;
		while (!Utility.FloatEqual(m_boss.transform.position.y, yTarget))
		{
			Vector3 pos = m_boss.transform.position;
			pos.y = Mathf.SmoothDamp(m_boss.transform.position.y, yTarget, ref ySpeedCur, smoothTimeSlow);
			m_boss.transform.position = pos;
			yield return null;
		}

		// reveal arms
		ArmController[] arms = m_boss.GetComponentsInChildren<ArmController>();
		float degrees = 0.0f;
		float angleVel = 0.0f;
		while (degrees < 359.0f)
		{
			Vector2 aimPos = m_boss.transform.position + Quaternion.Euler(0.0f, 0.0f, degrees) * Vector2.right;
			for (int i = (int)degrees * arms.Length / 360; i < arms.Length; ++i)
			{
				arms[i].UpdateAim(m_boss.m_armOffset, aimPos);
			}
			degrees = Mathf.SmoothDamp(degrees, 360.0f, ref angleVel, smoothTimeSlow);
			yield return null;
		}

		// zoom camera out
		Cinemachine.CinemachineVirtualCamera vCam = Camera.main.GetComponent<Cinemachine.CinemachineBrain>().ActiveVirtualCamera.VirtualCameraGameObject.GetComponent<Cinemachine.CinemachineVirtualCamera>();
		float zoomSpeedCur = 0.0f;
		float zoomSizeTarget = 5.0f;
		while (!Utility.FloatEqual(vCam.m_Lens.OrthographicSize, zoomSizeTarget))
		{
			vCam.m_Lens.OrthographicSize = Mathf.SmoothDamp(vCam.m_Lens.OrthographicSize, zoomSizeTarget, ref zoomSpeedCur, smoothTimeFast);
			yield return null;
		}

		// enable boss
		m_boss.GetComponent<Health>().m_invincible = false;
		m_boss.enabled = true;
	}
}
