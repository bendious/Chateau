using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent]
public class Boss : MonoBehaviour
{
	public BossRoom m_room;


	private Vector3 m_startPos;

	private bool m_started = false;
	private bool m_activatedFully = false;


	private void Awake()
	{
		m_startPos = transform.position;
		GetComponent<Health>().m_maxHP *= GameController.Instance.m_zoneScalar;
	}

	private void OnWillRenderObject()
	{
		if (!GameController.Instance.m_bossRoomSealed || m_started || GameController.IsSceneLoad)
		{
			return;
		}

		// ignore until we are well within camera view
		Vector2 screenPos = Camera.main.WorldToViewportPoint(transform.position);
		const float edgePct = 0.1f; // TODO: parameterize?
		if (screenPos.x < edgePct || screenPos.x > 1.0f - edgePct || screenPos.y < edgePct || screenPos.y > 1.0f - edgePct)
		{
			return;
		}

		m_started = true;
		StopAllCoroutines();
		StartCoroutine(UpdateIntro());
	}

	private void OnBecameInvisible()
	{
		// if we're in the pause before true activation, wait until we're back on camera
		if (m_started && !m_activatedFully)
		{
			StopAllCoroutines();
			m_started = false;
		}
	}

	private void OnDestroy()
	{
		if (GameController.IsSceneLoad || GameController.Instance.m_gameOverUI.isActiveAndEnabled)
		{
			return;
		}

		m_room.EndFight();

		// spawn ladder(s) if necessary
		RoomController roomComp = m_room.GetComponent<RoomController>();
		foreach (System.Tuple<GameObject, RoomController> doorway in roomComp.DoorwaysUpwardOpen)
		{
			// pathfind to skip unnecessary ladders
			System.Collections.Generic.List<Vector2> path = GameController.Instance.Pathfind(m_room.transform.position, doorway.Item2.transform.position, obstructionChecking: RoomController.ObstructionCheck.Directional); // NOTE that this has to be AFTER BossRoom.EndFight() unseals the room
			if (path != null)
			{
				continue;
			}

			roomComp.SpawnLadder(doorway.Item1, m_room.m_spawnedLadderPrefabs.RandomWeighted(), true);
		}

		GameController.Instance.OnVictory();
	}


#if DEBUG
	public void DebugReset()
	{
		StopAllCoroutines();
		m_started = false;
		m_activatedFully = false;
		EnemyController enemy = GetComponent<EnemyController>();
		enemy.m_passive = true;
		enemy.gravityModifier = 1.0f;
		enemy.Teleport(m_startPos);
		GetComponent<Health>().m_invincible = true;
	}
#endif


	private IEnumerator UpdateIntro()
	{
		const float smoothTimeSlow = 0.5f;

		// pause
		yield return new WaitForSeconds(2.0f);

		m_activatedFully = true;

		// adjust camera
		GameController.Instance.AddCameraTargets(transform);

		// recolor
		SpriteRenderer[] bossRenderers = GetComponentsInChildren<SpriteRenderer>().Where(renderer => renderer.GetComponent<ItemController>() == null).ToArray();
		Light2D bossLight = GetComponent<Light2D>();
		const float intensityTarget = 1.0f;
		Vector4 colorSpeedCur = Vector4.zero;
		float lightSpeedCur = 0.0f;
		while (!bossRenderers.First().color.ColorsSimilar(Color.red) || !bossLight.intensity.FloatEqual(intensityTarget)) // TODO: don't assume colors are always in sync?
		{
			foreach (SpriteRenderer bossRenderer in bossRenderers)
			{
				bossRenderer.color = bossRenderer.color.SmoothDamp(Color.red, ref colorSpeedCur, smoothTimeSlow);
			}
			bossLight.intensity = Mathf.SmoothDamp(bossLight.intensity, intensityTarget, ref lightSpeedCur, smoothTimeSlow);
			yield return null;
		}

		// float into air
		EnemyController enemy = GetComponent<EnemyController>();
		enemy.gravityModifier = 0.0f;
		float yTarget = transform.position.y + 4.5f; // TODO: unhardcode?
		float ySpeedCur = 0.0f;
		while (!transform.position.y.FloatEqual(yTarget))
		{
			Vector3 pos = transform.position;
			pos.y = Mathf.SmoothDamp(transform.position.y, yTarget, ref ySpeedCur, smoothTimeSlow);
			transform.position = pos;
			yield return null;
		}

		// reveal arms
		ArmController[] arms = GetComponentsInChildren<ArmController>();
		float degrees = 0.0f;
		float angleVel = 0.0f;
		while (degrees < 359.0f)
		{
			Vector2 aimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, degrees) * Vector2.right;
			for (int i = (int)degrees * arms.Length / 360; i < arms.Length; ++i)
			{
				arms[i].UpdateAim(GetComponent<KinematicCharacter>().ArmOffset, aimPos, aimPos, false);
			}
			degrees = Mathf.SmoothDamp(degrees, 360.0f, ref angleVel, smoothTimeSlow);
			yield return null;
		}

		m_room.AmbianceToMusic();

		// enable boss
		GetComponent<Health>().m_invincible = false;
		enemy.m_passive = false;
		GameController.Instance.EnemyAdd(enemy);
	}
}
