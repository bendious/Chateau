using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;


[DisallowMultipleComponent]
public class Boss : MonoBehaviour
{
	public BossRoom m_room;

	[SerializeField] private float m_smoothTimeSlow = 0.5f;
	[SerializeField] private float m_lightIntensityFinal = 1.0f;
	[SerializeField] private float m_floatHeight = 4.5f;
	[SerializeField] private float m_blinkSecondsMin = 0.5f;
	[SerializeField] private float m_blinkSecondsMax = 10.0f;

	[SerializeField] private Dialogue m_dialogue;
	public Dialogue m_dialogueFinal;

	public Sprite m_dialogueSprite;

	public WeightedObject<AudioClip>[] m_dialogueSfx;


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

		Vector2 closestAvatarDiff = (Vector2)GameController.Instance.m_avatars.SelectMin(avatar => Vector2.Distance(avatar.transform.position, transform.position)).transform.position - (Vector2)transform.position; // TODO: don't re-choose every frame?
		Vector2 aimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, m_introAimDegrees + Utility.ZDegrees(closestAvatarDiff)) * Vector2.right;
		bool hasOffset = m_ai.m_offsetDegreesMaxPerArm > 0.0f;
		for (int i = hasOffset ? (int)m_introAimDegrees * m_arms.Length / 360 : 0; i < m_arms.Length; ++i)
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
			// NOTE that this has to be AFTER BossRoom.EndFight() (at least partially-)unseals the room
			const RoomController.PathFlags pathFlags = RoomController.PathFlags.ObstructionCheck | RoomController.PathFlags.Directional;
			if (GameController.Instance.Pathfind(m_room.gameObject, doorway.Item2.gameObject, flags: pathFlags) != null)
			{
				continue;
			}

			roomComp.SpawnLadder(doorway.Item1, m_room.m_spawnedLadderPrefabs.RandomWeighted(), true);
		}

		GameController.Instance.OnVictory();
	}


	[System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "called via DialogueController.OnReplySelected()/SendMessage(Line.Reply.m_eventName, Line.Reply)")]
	public void AllowFinalDialogue(DialogueController.Line.Reply reply) => m_ai.AddAllowedState(AIState.Type.FinalDialogue);


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

		// enable initially disabled components
		SpriteMask mask = GetComponent<SpriteMask>();
		Animator animator = GetComponent<Animator>();
		if (mask != null)
		{
			mask.enabled = true;
			StartCoroutine(EyeManagementCoroutine(mask, animator)); // TODO: parameterize/vary?
		}
		m_health.m_gradientActive = true;
		animator.enabled = true;

		// recolor
		SpriteRenderer[] bossRenderers = GetComponentsInChildren<SpriteRenderer>().Where(renderer =>
		{
			if (renderer.GetComponent<ItemController>() != null)
			{
				return false;
			}
			ArmController arm = renderer.GetComponent<ArmController>();
			return arm == null || arm.m_colorMatching;
		}).ToArray();
		Light2D bossLight = GetComponent<Light2D>();
		Vector4[] colorSpeedCur = new Vector4[bossRenderers.Length + 1];
		float lightSpeedCur = 0.0f;
		while (!bossRenderers.First().color.ColorsSimilar(m_colorFinal) || !bossLight.intensity.FloatEqual(m_lightIntensityFinal)) // TODO: don't assume colors are always in sync?
		{
			int colorSpeedIdx = 0;
			foreach (SpriteRenderer bossRenderer in bossRenderers)
			{
				bossRenderer.color = bossRenderer.color.SmoothDamp(m_colorFinal, ref colorSpeedCur[colorSpeedIdx++], m_smoothTimeSlow);
			}
			bossLight.intensity = Mathf.SmoothDamp(bossLight.intensity, m_lightIntensityFinal, ref lightSpeedCur, m_smoothTimeSlow);
			bossLight.color = bossLight.color.SmoothDamp(m_colorFinal, ref colorSpeedCur[colorSpeedIdx++], m_smoothTimeSlow);
			yield return null;
		}

		// enable components needed after color/visibility change
		GetComponent<Collider2D>().enabled = true;

		if (m_floatHeight > 0.0f)
		{
			// float into air
			m_ai.gravityModifier = 0.0f;
			float yTarget = transform.position.y + m_floatHeight;
			float ySpeedCur = 0.0f;
			while (!transform.position.y.FloatEqual(yTarget))
			{
				Vector3 pos = transform.position;
				pos.y = Mathf.SmoothDamp(transform.position.y, yTarget, ref ySpeedCur, m_smoothTimeSlow);
				transform.position = pos;
				yield return null;
			}
		}

		// reveal arms / roll eyes
		// (see also FixedUpdate())
		m_introAimDegrees = 0.0f;
		yield return new WaitWhile(() => IsArmReveal);

		// dialogue
		// see Synchronous Coroutines in https://www.alanzucconi.com/2017/02/15/nested-coroutines-in-unity/
		// TODO: parameterize dialogue?
		yield return GameController.Instance.m_dialogueController.Play(m_dialogue.m_dialogue.RandomWeighted().m_lines, gameObject, GameController.Instance.m_avatars.First(), m_ai, m_dialogueSprite, m_colorFinal, m_dialogueSfx.Length <= 0 ? null : m_dialogueSfx.RandomWeighted(), expressionSets: m_dialogue.m_expressions); // TODO: take any preconditions into account?

		m_room.AmbianceToMusic();

		// enable boss
		m_health.m_invincible = false;
		m_ai.m_passive = false;
		GameController.Instance.EnemyAdd(m_ai);
		GameController.Instance.EnemyAddToWave(m_ai);
	}

	private IEnumerator EyeManagementCoroutine(SpriteMask mask, Animator animator)
	{
		SpriteRenderer renderer = GetComponent<SpriteRenderer>();
		float blinkTimeNext = Time.time + Random.Range(m_blinkSecondsMax * 0.5f, m_blinkSecondsMax); // TODO: better initial minimum based on intro length?

		while (true)
		{
			mask.sprite = renderer.sprite;

			if (Time.time >= blinkTimeNext)
			{
				animator.SetTrigger("blink"); // TODO: un-hardcode?
				blinkTimeNext = Time.time + Random.Range(m_blinkSecondsMin, m_blinkSecondsMax);
			}

			yield return null;
		}
	}
}
