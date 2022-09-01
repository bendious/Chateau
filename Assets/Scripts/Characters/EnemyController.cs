using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// A simple controller for enemies. Provides state updating, aiming, movement control toward a target object, etc.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyController : KinematicCharacter
{
	[SerializeField] private AIState.Type[] m_allowedStates = new[] { AIState.Type.Pursue, AIState.Type.Flee, AIState.Type.RamSwoop };

	public bool m_passive;
	public bool m_friendly;

	public Vector2 m_targetOffset = Vector2.zero;
	public Component m_target;
	[SerializeField] private float m_replanSecondsMax = 3.0f;

	public float m_meleeRange = 1.0f;

	[SerializeField] private float m_jumpMaxSpeedOverride = -1.0f;
	[SerializeField] private bool m_airControl = true;
	[SerializeField] private bool m_jumpAlways;
	[SerializeField] private float m_jumpPct = 0.05f;

	[SerializeField] private float m_dropDecayTime = 0.2f;

	[SerializeField] private AudioClip[] m_attackSFX; // TODO: remove in favor of animation triggers w/ AudioCollection?
	public WeightedObject<GameObject>[] m_teleportVFX;


	[SerializeField] private GameObject m_heldPrefab;


	public float AimOffsetDegrees { private get; set; }
	public float AimScalar { private get; set; } = 1.0f;


	private int m_aimLastFrame; // OPTIMIZATION: only aim arms once per frame even if physics is stepping more

	private float m_targetSelectTimeNext;
	private AIState m_aiState;

	private float m_pathfindTimeNext;
	private List<Vector2> m_pathfindWaypoints;

	private float m_dropDecayVel;


	private bool ShouldSkipUpdates => m_passive || ConsoleCommands.PassiveAI || HasForcedVelocity; // TODO: decouple AI process pausing from forced velocity?


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
		StartCoroutine(SpawnHeldItemsWhenActive());
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

		if (m_passive) // NOTE that though arms should generally be updated even when passive, boss intros rely on that not being the case... // TODO: decouple?
		{
			return;
		}

		// aim items
		if (HoldCountMax > 0 && m_aimLastFrame != Time.frameCount)
		{
			ArmController[] arms = GetComponentsInChildren<ArmController>();
			if (arms.Length > 0)
			{
				ArmController primaryArm = arms.FirstOrDefault(arm => arm.GetComponentInChildren<ItemController>() != null);
				Vector2 targetPosSafe = AimPosition();
				if (primaryArm != null)
				{
					primaryArm.UpdateAim(ArmOffset, targetPosSafe, targetPosSafe, false);
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
					arm.UpdateAim(ArmOffset, aimPos, targetPosSafe, false);
				}
			}
			m_aimLastFrame = Time.frameCount;
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
				int[] lineIndicies = m_pathfindWaypoints.SelectMany(vec2 => new[] { i, ++i }).ToArray()[0 .. ^2]; // i.e. [0, 1, 1, 2, 2, 3, ..., WaypointCount - 2, WaypointCount - 1]
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
		if (m_targetSelectTimeNext <= Time.time && m_aiState != null)
		{
			m_aiState.Retarget();
			m_targetSelectTimeNext = Time.time + Random.Range(m_replanSecondsMax * 0.5f, m_replanSecondsMax); // TODO: parameterize "min" time even though it's not a hard minimum?
		}
		if (m_target == null || ShouldSkipUpdates)
		{
			move = Vector2.zero;
			return false; // TODO: flag to trigger idle behavior if unable to find target?
		}

		// pathfind
		// TODO: efficiency?
		if (m_pathfindTimeNext <= Time.time || (m_pathfindWaypoints != null && m_pathfindWaypoints.Count > 0 && !Vector2.Distance(m_target.transform.position, m_pathfindWaypoints.Last()).FloatEqual(targetOffsetAbs.magnitude, m_meleeRange))) // TODO: better re-plan trigger(s) (more precise as distance remaining decreases); avoid trying to go past moving targets?
		{
			m_pathfindWaypoints = GameController.Instance.Pathfind(gameObject, m_target.gameObject, m_collider.bounds.extents.y, !HasFlying && jumpTakeOffSpeed <= 0.0f ? 0.0f : float.MaxValue, targetOffsetAbs, RoomController.PathFlags.ObstructionCheck | (HasFlying ? RoomController.PathFlags.IgnoreGravity : RoomController.PathFlags.None)); // TODO: limit to max jump height once pathfinding takes platforms into account?
			if (m_pathfindWaypoints == null)
			{
				m_target = null; // TODO: better handle unreachable positions; idle? find closest reachable position?
			}
			else if (GameController.Instance.m_avatars.Any(avatar => avatar.gameObject == m_target.gameObject))
			{
				// if targeting and successfully pathfinding to an avatar, ensure we are now contained in GameController.m_enemiesActive[]
				// TODO: efficiency?
				GameController.Instance.EnemyAdd(this);
			}
			m_pathfindTimeNext = Time.time + Random.Range(m_replanSecondsMax * 0.5f, m_replanSecondsMax); // TODO: parameterize "min" time even though it's not a hard minimum?
		}
		if (m_pathfindWaypoints == null || m_pathfindWaypoints.Count == 0)
		{
			// NOTE that even if targeting an avatar and unable to pathfind to any avatars, we don't have to remove ourself from GameController.m_enemiesActive[] to prevent wave softlocks, since GameController.SpawnEnemyWaveCoroutine() takes care of that
			move = Vector2.zero;
			return false;
		}

		Collider2D targetCollider = m_target.GetComponent<Collider2D>(); // TODO: efficiency?
		Vector2 halfExtentsCombined = (m_collider.bounds.extents + targetCollider.bounds.extents) * 0.5f;

		// process & check for arrival at current waypoint(s)
		Vector2 nextWaypoint;
		Vector2 diff;
		bool atWaypoint;
		do
		{
			// get relative position
			nextWaypoint = m_pathfindWaypoints.First();
			diff = nextWaypoint - (Vector2)transform.position;

			// prevent jittering
			if (Mathf.Abs(diff.x) < halfExtentsCombined.x)
			{
				diff.x = 0.0f;
			}
			if (Mathf.Abs(diff.y) < halfExtentsCombined.y)
			{
				diff.y = 0.0f;
			}

			// check arrival
			const float arrivalEpsilon = 0.1f; // TODO: derive/calculate?
			atWaypoint = diff.magnitude <= ((Vector2)m_collider.bounds.extents).magnitude + m_collider.offset.magnitude + arrivalEpsilon;
			if (atWaypoint)
			{
				m_pathfindWaypoints.RemoveAt(0);
			}
		}
		while (atWaypoint && m_pathfindWaypoints.Count > 0);

		// determine current direction
		Vector2 dir = diff.normalized;
		if ((IsGrounded || m_airControl) && maxSpeed != 0.0f)
		{
			if (HasFlying)
			{
				move.x = dir.x;
			}
			else
			{
				move.x = System.Math.Sign(dir.x); // NOTE that Mathf's version of Sign() treats zero as positive...
				if (m_jumpMaxSpeedOverride >= 0.0f && !IsGrounded)
				{
					move.x *= m_jumpMaxSpeedOverride / maxSpeed; // TODO: support overriding maxSpeed of zero?
				}
			}
		}

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
			if (IsGrounded && (m_jumpAlways || nextBounds.min.y > selfBounds.max.y) && Random.value <= m_jumpPct)
			{
				m_jump = 1.0f;
				if (!m_airControl)
				{
					move.x *= m_jumpMaxSpeedOverride / maxSpeed;

					// determine how hard to jump
					float gravityEffective = -9.81f * gravityModifier; // TODO: determine why Physics2D.gravity is inaccurate; don't assume vertical gravity?
					float determinant = jumpTakeOffSpeed * jumpTakeOffSpeed - 4.0f * gravityEffective * -diff.y; // TODO: better determination of how far up/down we'll land if too far to make it to the waypoint?
					if (determinant < 0.0f)
					{
						determinant = jumpTakeOffSpeed * jumpTakeOffSpeed;
					}
					float jumpMaxDist = (-jumpTakeOffSpeed - Mathf.Sqrt(determinant)) / (2.0f * gravityEffective) * m_jumpMaxSpeedOverride;
					m_jump *= Mathf.Min(1.0f, diff.magnitude / jumpMaxDist); // TODO: separate x/y logic for improved upward movement?
				}
			}
			move.y = IsGrounded && nextBounds.max.y < selfBounds.min.y ? -1.0f : Mathf.SmoothDamp(move.y, 0.0f, ref m_dropDecayVel, m_dropDecayTime * 2.0f); // NOTE the IsGrounded check and damped decay (x2 since IsDropping's threshold is -0.5) to cause stopping at each ladder rung when descending
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
			m_audioSource.PlayOneShot(m_attackSFX.Random());
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
		m_target = GameController.Instance.m_avatars.First();
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
		Vector2 aimPos = m_target == null ? (Vector2)transform.position + (LeftFacing ? Vector2.left : Vector2.right) : m_target.transform.position;

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
			if (timeDiffApprox > 0.0f)
			{
				const float gravity = /*Physics2D.gravity.y*/-9.81f; // TODO: determine why Physics2D.gravity does not match the outcome
				float launchSlopePerSec = posDiff.y / timeDiffApprox - gravity * timeDiffApprox;
				aimPos.y = aimItem.transform.position.y + launchSlopePerSec * timeDiffApprox;
			}
		}

		return aimPos;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void EnablePlayerControl()
	{
	}
}
