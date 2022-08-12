using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// A simple controller for enemies. Provides state updating, aiming, movement control toward a target object, etc.
/// </summary>
[DisallowMultipleComponent]
public class EnemyController : KinematicCharacter
{
	public AIState.Type[] m_allowedStates = new AIState.Type[] { AIState.Type.Pursue, AIState.Type.Flee, AIState.Type.RamSwoop };

	public float m_contactDamage = 1.0f;

	public bool m_passive;
	public bool m_friendly;

	public Vector2 m_targetOffset = Vector2.zero;
	public Transform m_target;
	[SerializeField] private float m_replanSecondsMax = 2.0f;

	public float m_meleeRange = 1.0f;

	public float m_dropDecayTime = 0.2f;

	public AudioClip[] m_attackSFX; // TODO: remove in favor of animation triggers w/ AudioCollection?
	public WeightedObject<GameObject>[] m_teleportVFX;


	[SerializeField] private GameObject m_heldPrefab;


	public float AimOffsetDegrees { private get; set; }
	public float AimScalar { private get; set; } = 1.0f;


	private float m_targetSelectTimeNext;
	private AIState m_aiState;

	private float m_pathfindTimeNext;
	private List<Vector2> m_pathfindWaypoints;

	private float m_dropDecayVel;


	private bool ShouldSkipUpdates => m_passive || ConsoleCommands.PassiveAI || HasForcedVelocity;


	protected override void Awake()
	{
		base.Awake();

		OnHealthDecrement.OnExecute += OnDamage;
		OnHealthDeath.OnExecute += OnDeath;
	}

	protected override void Start()
	{
		base.Start();
		// TODO: spawn animation / fade-in?
		m_targetSelectTimeNext = Time.time + Random.Range(0.0f, m_replanSecondsMax);
		m_pathfindTimeNext = Time.time + Random.Range(0.0f, m_replanSecondsMax);

		if (m_heldPrefab == null)
		{
			return;
		}
		if (m_passive)
		{
			StartCoroutine(SpawnHeldItemsWhenActive());
		}
		else
		{
			_ = SpawnHeldItemsWhenActive(); // NOTE the return value discard to indicate that we are deliberately not using StartCoroutine() and don't need to be warned
		}
	}

	private void OnCollisionEnter2D(Collision2D collision)
	{
		var avatar = collision.collider.GetComponent<AvatarController>(); // NOTE that we use the collider object rather than collision.gameObject since w/ characters & arms, they are not always the same
		if (avatar != null)
		{
			avatar.OnEnemyCollision(this);
		}
	}

	private void OnCollisionStay2D(Collision2D collision)
	{
		var avatar = collision.collider.GetComponent<AvatarController>(); // NOTE that we use the collider object rather than collision.gameObject since w/ characters & arms, they are not always the same
		if (avatar != null)
		{
			avatar.OnEnemyCollision(this);
		}
	}

	protected override void Update()
	{
		if (ShouldSkipUpdates)
		{
			move = Vector2.zero;
		}
		else
		{
			if (m_aiState == null)
			{
				m_aiState = AIState.FromTypePrioritized(m_allowedStates, this);
				m_aiState.Enter();
			}

			AIState stateNew = m_aiState.Update();
			if (stateNew != m_aiState)
			{
				// TODO: split across frames?
				m_aiState.Exit();
				m_aiState = stateNew;
				if (m_aiState != null)
				{
					m_aiState.Enter();
				}
			}
		}

		base.Update();
	}

	protected override void FixedUpdate()
	{
		base.FixedUpdate();

		if (ShouldSkipUpdates)
		{
			return;
		}

		// aim items
		if (HoldCountMax > 0)
		{
			ArmController[] arms = GetComponentsInChildren<ArmController>();
			if (arms.Length > 0)
			{
				ArmController primaryArm = arms.FirstOrDefault(arm => arm.GetComponentInChildren<ItemController>() != null);
				Vector2 targetPosSafe = AimPosition();
				if (primaryArm != null)
				{
					primaryArm.UpdateAim(ArmOffset, targetPosSafe, targetPosSafe);
				}

				int offsetScalar = primaryArm == null ? 0 : 1;
				foreach (ArmController arm in arms.OrderBy(arm => -arm.transform.childCount)) // NOTE the ordering to aim non-empty arms first
				{
					if (arm == primaryArm)
					{
						continue; // primaryArm is already aimed
					}
					Vector2 aimPos = transform.position + Quaternion.Euler(0.0f, 0.0f, offsetScalar * System.Math.Min(60, 360 / arms.Length) * AimScalar) * (targetPosSafe - (Vector2)transform.position); // TODO: remove hardcoded max?
					offsetScalar = offsetScalar <= 0 ? -offsetScalar + 1 : -offsetScalar; // this groups any arms w/ items around the primary arm in both directions
					arm.UpdateAim(ArmOffset, aimPos, targetPosSafe);
				}
			}
		}
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (m_aiState != null)
		{
			m_aiState.DebugGizmo();
		}

		if (ConsoleCommands.AIDebugLevel >= (int)ConsoleCommands.AIDebugLevels.Positions)
		{
			if (m_pathfindWaypoints?.Count > 0)
			{
				UnityEditor.Handles.DrawLine(transform.position, m_pathfindWaypoints.First());
				int i = 0;
				int[] lineIndicies = m_pathfindWaypoints.SelectMany(vec2 => new int[] { i, ++i }).ToArray()[0..^2]; // i.e. [0, 1, 1, 2, 2, 3, ..., WaypointCount - 2, WaypointCount - 1]
				UnityEditor.Handles.DrawLines(m_pathfindWaypoints.Select(vec2 => (Vector3)vec2).ToArray(), lineIndicies);
			}

			if (HoldCountMax > 0)
			{
				UnityEditor.Handles.DrawWireArc(AimPosition(), Vector3.forward, Vector3.right, 360.0f, 0.1f);
			}
		}
	}
#endif

	protected override void OnDestroy()
	{
		base.OnDestroy();

		OnHealthDecrement.OnExecute -= OnDamage;
		OnHealthDeath.OnExecute -= OnDeath;
	}


	public override bool CanDamage(GameObject target)
	{
		if (!base.CanDamage(target))
		{
			return false;
		}
		if (m_friendly && target.GetComponent<AvatarController>() != null)
		{
			return false;
		}
		EnemyController otherEnemy = target.GetComponent<EnemyController>();
		if (otherEnemy != null && otherEnemy.m_friendly == m_friendly)
		{
			return false;
		}
		return true;
	}

	public override float TargetPriority(KinematicCharacter source) => m_passive ? 0.0f : base.TargetPriority(source);


	private void OnDamage(OnHealthDecrement evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}

		AIState stateNew = m_aiState?.OnDamage(evt.m_damageSource);
		if (stateNew != m_aiState)
		{
			// NOTE that we can't split this across frames since we might not get another Update() call due to death
			m_aiState.Exit();
			m_aiState = stateNew;
			if (m_aiState != null)
			{
				m_aiState.Enter();
			}
		}
	}

	private void OnDeath(OnHealthDeath evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}
		enabled = false;
	}


	// TODO: un-expose?
	public bool NavigateTowardTarget(Vector2 targetOffsetAbs)
	{
		if (m_targetSelectTimeNext <= Time.time && (m_target == null || m_target.GetComponent<KinematicCharacter>() != null))
		{
			// choose appropriate target
			// TODO: use pathfind distances? allow re-targeting other types of targets?
			float sqDistClosest = float.MaxValue;
			float priorityMax = float.Epsilon;
			foreach (KinematicCharacter character in GameController.Instance.AiTargets)
			{
				float priority = character.TargetPriority(this);
				if (priority < priorityMax)
				{
					continue;
				}
				Transform charTf = character.transform;
				float sqDist = Vector2.Distance(transform.position, charTf.position);
				if (priority > priorityMax || sqDist < sqDistClosest)
				{
					sqDistClosest = sqDist;
					priorityMax = priority;
					m_target = charTf;
				}
			}

			m_targetSelectTimeNext = Time.time + Random.Range(m_replanSecondsMax * 0.5f, m_replanSecondsMax); // TODO: parameterize "min" time even though it's not a hard minimum?
		}
		if (m_target == null || ShouldSkipUpdates)
		{
			move = Vector2.zero;
			return false; // TODO: flag to trigger idle behavior if unable to find target?
		}

		// pathfind
		// TODO: efficiency?
		if (m_pathfindTimeNext <= Time.time || (m_pathfindWaypoints != null && m_pathfindWaypoints.Count > 0 && !Vector2.Distance(m_target.position, m_pathfindWaypoints.Last()).FloatEqual(targetOffsetAbs.magnitude, m_meleeRange))) // TODO: better re-plan trigger(s) (more precise as distance remaining decreases); avoid trying to go past moving targets?
		{
			m_pathfindWaypoints = GameController.Instance.Pathfind(transform.position, m_target.position, targetOffsetAbs, m_collider.bounds.extents.y);
			if (m_pathfindWaypoints == null)
			{
				m_target = null; // TODO: better handle unreachable positions; idle? find closest reachable position?
			}
			m_pathfindTimeNext = Time.time + Random.Range(m_replanSecondsMax * 0.5f, m_replanSecondsMax); // TODO: parameterize "min" time even though it's not a hard minimum?
		}
		if (m_pathfindWaypoints == null || m_pathfindWaypoints.Count == 0)
		{
			move = Vector2.zero;
			return false;
		}
		Vector2 nextWaypoint = m_pathfindWaypoints.First();

		// determine current direction
		Vector2 diff = nextWaypoint - (Vector2)transform.position;
		Collider2D targetCollider = m_target.GetComponent<Collider2D>(); // TODO: efficiency?
		Vector2 halfExtentsCombined = (m_collider.bounds.extents + targetCollider.bounds.extents) * 0.5f;
		if (Mathf.Abs(diff.x) < halfExtentsCombined.x)
		{
			diff.x = 0.0f;
		}
		if (Mathf.Abs(diff.y) < halfExtentsCombined.y)
		{
			diff.y = 0.0f;
		}
		Vector2 dir = diff.normalized;

		// left/right
		move.x = HasFlying ? dir.x : System.Math.Sign(dir.x); // NOTE that Mathf's version of Sign() treats zero as positive...

		if (HasFlying)
		{
			// fly
			move.y = dir.y;
		}
		else
		{
			// jump/drop
			// TODO: only jump when directly below, but w/o getting stuck?
			Bounds nextBounds = targetCollider == null || m_pathfindWaypoints.Count > 1 ? new(nextWaypoint, Vector3.zero) : targetCollider.bounds;
			Bounds selfBounds = m_collider.bounds;
			if (IsGrounded && nextBounds.min.y > selfBounds.max.y && Random.value > 0.95f/*?*/)
			{
				m_jump = true;
			}
			move.y = IsGrounded && nextBounds.max.y < selfBounds.min.y ? -1.0f : Mathf.SmoothDamp(move.y, 0.0f, ref m_dropDecayVel, m_dropDecayTime * 2.0f); // NOTE the IsGrounded check and damped decay (x2 since IsDropping's threshold is -0.5) to cause stopping at each ladder rung when descending
		}

		const float arrivalEpsilon = 0.1f; // TODO: derive/calculate?
		if (diff.magnitude <= ((Vector2)m_collider.bounds.extents).magnitude + m_collider.offset.magnitude + arrivalEpsilon)
		{
			m_pathfindWaypoints.RemoveAt(0);
		}
		return m_pathfindWaypoints.Count == 0;
	}

	public void ClearPath(bool immediateRepath)
	{
		if (immediateRepath)
		{
			m_pathfindTimeNext = Time.time;
		}
		m_pathfindWaypoints = null;
	}

	public void PlayAttackEffects()
	{
		m_animator.SetBool("attacking", true);

		if (m_attackSFX.Length > 0)
		{
			m_audioSource.PlayOneShot(m_attackSFX[Random.Range(0, m_attackSFX.Length)]);
		}
	}

	public void StopAttackEffects()
	{
		m_animator.SetBool("attacking", false);
		// TODO: cut off SFX if appropriate?
	}

#if DEBUG
	public void DebugPathfindTest()
	{
		m_target = GameController.Instance.m_avatars.First().transform;
		if (m_aiState != null)
		{
			m_aiState.Exit();
		}
		m_aiState = new AIPursue(this);
		m_aiState.Enter();
	}
#endif


	private System.Collections.IEnumerator SpawnHeldItemsWhenActive()
	{
		if (m_passive)
		{
			yield return new WaitUntil(() => !m_passive);
		}

		foreach (ArmController arm in GetComponentsInChildren<ArmController>())
		{
			if (arm.transform.childCount > 0)
			{
				continue;
			}
			arm.ChildAttach(GameController.Instance.m_savableFactory.Instantiate(m_heldPrefab, transform.position, transform.rotation).GetComponent<ItemController>());
		}

		// clear AI state in case we had just planned to go get items
		if (m_aiState != null)
		{
			m_aiState.Exit();
			m_aiState = null;
		}
	}

	private Vector2 AimPosition()
	{
		// TODO: option to stop tracking target during certain actions?

		// base target position
		Vector2 aimPos = m_target == null ? (Vector2)transform.position + (LeftFacing ? Vector2.left : Vector2.right) : m_target.position;

		// aim directly if no arms/items
		ArmController[] arms = GetComponentsInChildren<ArmController>();
		if (arms.Length <= 0)
		{
			return aimPos;
		}
		ItemController aimItem = null;
		arms.FirstOrDefault(arm =>
		{
			aimItem = arm.GetComponentInChildren<ItemController>();
			return aimItem != null;
		});
		if (aimItem == null)
		{
			return aimPos;
		}

		if (AimOffsetDegrees != 0.0f)
		{
			// add rotational offset
			aimPos = (Vector2)(Quaternion.Euler(0.0f, 0.0f, AimOffsetDegrees) * (aimPos - (Vector2)transform.position)) + (Vector2)transform.position;
		}
		else
		{
			// approximate the parabolic trajectory
			// given ax^2 + bx + c = 0, b = (-c - ax^2) / x = -c/x - ax
			Vector2 posDiff = aimPos - (Vector2)aimItem.transform.position;
			float timeDiffApprox = posDiff.magnitude / aimItem.m_throwSpeed;
			float gravity = /*Physics2D.gravity.y*/-9.81f; // TODO: determine why Physics2D.gravity does not match the outcome
			float launchSlopePerSec = posDiff.y / timeDiffApprox - gravity * timeDiffApprox;
			aimPos.y = aimItem.transform.position.y + launchSlopePerSec * timeDiffApprox;
		}

		return aimPos;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void EnablePlayerControl()
	{
	}
}
