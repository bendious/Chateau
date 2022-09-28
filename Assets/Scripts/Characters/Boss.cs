using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent]
public class Boss : MonoBehaviour
{
	public BossRoom m_room;

	[SerializeField] private float m_smoothTimeSlow = 0.5f;

	[SerializeField] private Sprite m_dialogueSprite;

	[SerializeField] private WeightedObject<AudioClip>[] m_dialogueSfx;


#if DEBUG
	private Vector3 m_debugStartPos;
#endif
	private AIController m_ai;
	ArmController[] m_arms;
	private Health m_health;
	private Color m_colorFinal;

	private bool m_started = false;
	private bool m_activatedFully = false;

	private float m_introAimDegrees = -1.0f;
	private float m_introAimVel;


	private bool IsArmReveal => m_introAimDegrees >= 0.0f && m_introAimDegrees < 359.0f;


	private void Awake()
	{
#if DEBUG
		m_debugStartPos = transform.position;
#endif
		m_ai = GetComponent<AIController>();
		m_arms = GetComponentsInChildren<ArmController>();
		m_health = GetComponent<Health>();
		m_health.SetMax(m_health.GetMax() * GameController.Instance.m_zoneScalar);
		m_colorFinal = m_health.ColorCurrent;
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

	private void FixedUpdate()
	{
		if (!IsArmReveal)
		{
			return;
		}

		Vector2 aimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, m_introAimDegrees) * Vector2.right;
		for (int i = (int)m_introAimDegrees * m_arms.Length / 360; i < m_arms.Length; ++i)
		{
			m_arms[i].UpdateAim(m_ai.ArmOffset, aimPos, aimPos);
		}
		m_introAimDegrees = Mathf.SmoothDamp(m_introAimDegrees, 360.0f, ref m_introAimVel, m_smoothTimeSlow);
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
			// NOTE that this has to be AFTER BossRoom.EndFight() unseals the room
			const RoomController.PathFlags pathFlags = RoomController.PathFlags.ObstructionCheck | RoomController.PathFlags.Directional;
			if (GameController.Instance.Pathfind(m_room.gameObject, doorway.Item2.gameObject, flags: pathFlags) != null)
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
		m_ai.m_passive = true;
		m_ai.gravityModifier = 1.0f;
		m_ai.Teleport(m_debugStartPos);
		m_health.m_invincible = true;
	}
#endif


	private IEnumerator UpdateIntro()
	{
		// pause
		yield return new WaitForSeconds(2.0f);

		m_activatedFully = true;

		// adjust camera
		GameController.Instance.AddCameraTargets(transform);

		// recolor
		SpriteRenderer[] bossRenderers = GetComponentsInChildren<SpriteRenderer>().Where(renderer => renderer.GetComponent<ItemController>() == null).ToArray();
		Light2D bossLight = GetComponent<Light2D>();
		const float intensityTarget = 1.0f;
		Vector4[] colorSpeedCur = new Vector4[bossRenderers.Length + 1];
		float lightSpeedCur = 0.0f;
		while (!bossRenderers.First().color.ColorsSimilar(m_colorFinal) || !bossLight.intensity.FloatEqual(intensityTarget)) // TODO: don't assume colors are always in sync?
		{
			int colorSpeedIdx = 0;
			foreach (SpriteRenderer bossRenderer in bossRenderers)
			{
				bossRenderer.color = bossRenderer.color.SmoothDamp(m_colorFinal, ref colorSpeedCur[colorSpeedIdx++], m_smoothTimeSlow);
			}
			bossLight.intensity = Mathf.SmoothDamp(bossLight.intensity, intensityTarget, ref lightSpeedCur, m_smoothTimeSlow);
			bossLight.color = bossLight.color.SmoothDamp(m_colorFinal, ref colorSpeedCur[colorSpeedIdx++], m_smoothTimeSlow);
			yield return null;
		}

		// float into air
		m_ai.gravityModifier = 0.0f;
		float yTarget = transform.position.y + 4.5f; // TODO: unhardcode?
		float ySpeedCur = 0.0f;
		while (!transform.position.y.FloatEqual(yTarget))
		{
			Vector3 pos = transform.position;
			pos.y = Mathf.SmoothDamp(transform.position.y, yTarget, ref ySpeedCur, m_smoothTimeSlow);
			transform.position = pos;
			yield return null;
		}

		// reveal arms
		// (see also FixedUpdate())
		m_introAimDegrees = 0.0f;
		yield return new WaitWhile(() => IsArmReveal);

		// dialogue
		// see Synchronous Coroutines in https://www.alanzucconi.com/2017/02/15/nested-coroutines-in-unity/
		// TODO: parameterize dialogue?
		yield return GameController.Instance.m_dialogueController.Play(new DialogueController.Line[] { new() { m_text = "...", m_replies = new DialogueController.Line.Reply[] { new() { m_text = "I will destroy you." }, new() { m_text = "I will not resist.", m_eventName = "DisablePlayerControlUntilHurt" } } } }, GameController.Instance.m_avatars.First(), m_dialogueSprite, m_colorFinal, sfx: m_dialogueSfx.Length <= 0 ? null : m_dialogueSfx.RandomWeighted());

		m_room.AmbianceToMusic();

		// enable boss
		m_health.m_invincible = false;
		m_ai.m_passive = false;
		GameController.Instance.EnemyAdd(m_ai);
	}
}
