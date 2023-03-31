using System.Collections.Generic;
using System.Linq;
using UnityEngine;


/// <summary>
/// A simple controller for enemies/NPCs. Provides state updating, aiming, movement control toward a target object, etc.
/// </summary>
[DisallowMultipleComponent]
public sealed class AIController : KinematicCharacter
{
	[SerializeField] private AIState.Type[] m_allowedStates = new[] { AIState.Type.Fraternize, AIState.Type.Pursue, AIState.Type.Flee, AIState.Type.RamSwoop };

	public bool m_passive;
	public bool m_noArmUpdates;
	public bool m_friendly;

	public float m_difficulty = 1.0f;

	public Vector2 m_targetOffset = Vector2.zero;
	public Component m_target;
	public float m_replanSecondsMin = 1.5f; // NOTE that this is just to discourage constant replanning and not a hard minimum; it can be preempted based on state change / etc.
	public float m_replanSecondsMax = 3.0f;

	public float m_meleeRange = 1.0f;
	public float m_meleeSecondsMin = 0.5f;
	public float m_meleeSecondsMax = 1.0f;
	[SerializeField] private float m_alertDistanceMax = 10.0f;

	[SerializeField] private float m_jumpMaxSpeedOverride = -1.0f;
	[SerializeField] private bool m_airControl = true;
	[SerializeField] private bool m_jumpAlways;
	[SerializeField] private float m_jumpPct = 0.05f;
	[SerializeField] private float m_jumpPctRapid = 0.25f;
	[SerializeField] private float m_theftDialoguePct = 0.5f; // TODO: vary / derive from assigned Dialogue types?

	[SerializeField] private float m_dropDecayTime = 0.2f;

	[SerializeField] private AudioClip[] m_attackSFX; // TODO: remove in favor of animation triggers w/ AudioCollection?
	public WeightedObject<GameObject>[] m_attackPrefabs;

	public WeightedObject<string>[] m_teleportTriggers;
	public float TeleportTime { get; private set; }
	public float TeleportTimeFinish { get; private set; }

	[SerializeField] private GameObject m_attentionFlagPrefab;


	[SerializeField] private WeightedObject<ItemController>[] m_heldItems;


	public bool AimFreeze { private get; set; }
	public float AimOffsetDegrees { private get; set; }
	public float AimScalar { private get; set; } = 1.0f;

	public bool OnlyPursueAvatar { get; private set; }


	private Vector2 m_aimPosition;
	private int m_aimLastFrame; // OPTIMIZATION: only aim arms once per frame even if physics is stepping more

	[HideInInspector] public float m_targetSelectTimeNext;
	private AIState m_aiState;

	[HideInInspector] public float m_pathfindTimeNext;
	private List<Vector2> m_pathfindWaypoints;

	private Vector2 m_targetOffsetOrig;

	private float m_dropDecayVel;

	private GameObject m_attentionFlag;


	private bool ShouldSkipUpdates => m_passive || ConsoleCommands.PassiveAI || HasForcedVelocity || Time.deltaTime <= 0.0f; // TODO: decouple AI process pausing from forced velocity?


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

		m_targetOffsetOrig = m_targetOffset;

		if (m_heldItems.Length <= 0)
		{
			return;
		}
		StartCoroutine(SpawnHeldItemsWhenActive());
	}

	protected override void Update()
	{
		if (ShouldSkipUpdates)
		{
			move = HasFlying || IsGrounded ? Vector2.zero : move;
		}
		else
		{
			if (m_aiState == null)
			{
				if (OnlyPursueAvatar)
				{
					m_aiState = new AIPursue(this);
				}
				else
				{
					m_aiState = AIState.FromTypePrioritized(m_allowedStates, this);
				}
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
				else
				{
					move = Vector2.zero;
				}
			}
		}

		base.Update();
	}

	protected override void FixedUpdate()
	{
		base.FixedUpdate();

		if (m_noArmUpdates) // NOTE that though arms should generally be updated even when passive, boss intros rely on manipulating the arms independently
		{
			return;
		}

		// aim items
		if (HoldCountMax > 0)
		{
			if (m_aimLastFrame != Time.frameCount)
			{
				UpdateAimPosition();
				m_aimLastFrame = Time.frameCount;
			}
			AimArms(m_aimPosition, AimOffsetDegrees, AimScalar);
		}
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		m_aiState?.DebugGizmo();

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
				UnityEditor.Handles.DrawWireArc(m_aimPosition, Vector3.forward, Vector3.right, 360.0f, 0.1f);
			}
		}
	}
#endif

	protected override void OnDestroy()
	{
		base.OnDestroy();

		OnHealthDecrement.OnExecute -= OnDamage;
		OnHealthDeath.OnExecute -= OnDeath;

		m_aiState?.Exit(); // in case the AIState manages external things (e.g. AIFinalDialogue loads a scene)
	}


	public override bool CanDamage(GameObject target)
	{
		if (!base.CanDamage(target))
		{
			return false;
		}
		if (m_friendly && target.GetComponentInParent<AvatarController>() != null)
		{
			return false;
		}
		AIController otherAI = target.GetComponentInParent<AIController>();
		if (otherAI != null && otherAI.m_friendly == m_friendly)
		{
			return false;
		}
		return true;
	}

	// TODO: CanBeDamagedBy()?

	public override float TargetPriority(KinematicCharacter source, bool friendly) => m_passive ? 0.0f : base.TargetPriority(source, friendly);


	public void OnChildDetached(IHolder holderNew)
	{
		if (holderNew == null)
		{
			return;
		}
		if (holderNew is KinematicCharacter otherCharacter || (holderNew.Component.transform.parent != null && holderNew.Component.transform.parent.TryGetComponent(out otherCharacter))) // TODO: support holders attached to characters in arbitrary ways?
		{
			if (TryGetComponent(out InteractNpc npc) && Random.value < m_theftDialoguePct)
			{
				npc.StartDialogue(otherCharacter, Dialogue.Info.Type.Theft);
			}
		}
	}

	private void OnDamage(OnHealthDecrement evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}

		AIState stateNew = m_aiState?.OnDamage(evt.m_damageSource, evt.m_amountUnscaled);
		if (stateNew != m_aiState)
		{
			// NOTE that we can't split this across frames since we might not get another Update() call due to death
			m_aiState.Exit();
			m_aiState = stateNew;
			m_aiState?.Enter();
		}
	}

	private void OnDeath(OnHealthDeath evt)
	{
		if (evt.m_health.gameObject != gameObject)
		{
			return;
		}
		m_passive = true;
	}


	public void SetOnlyPursueAvatar(bool active)
	{
		OnlyPursueAvatar = active;

		// manage attention flag
		if (active && m_attentionFlag == null)
		{
			m_attentionFlag = Instantiate(m_attentionFlagPrefab, transform);
		}
		else if (!active && m_attentionFlag != null)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = m_attentionFlag;
			m_attentionFlag = null;
		}

		m_targetOffset = active ? Vector2.right : m_targetOffsetOrig;

		// replan
		if (m_aiState != null)
		{
			m_aiState.Exit();
			m_aiState = null;
		}
		m_target = null; // to avoid subsequent goal automatically using the avatar as the target even if it should only target enemies
	}

	public void AddAllowedState(AIState.Type type) => m_allowedStates = m_allowedStates.Append(type).ToArray();


	// TODO: un-expose?
	public bool NavigateTowardTarget(Vector2 targetOffsetAbs)
	{
		Vector2 waypointDiff;
		bool isAtWaypoint(Vector2 waypoint, Collider2D targetCollider, bool isStartingPoint)
		{
			waypointDiff = waypoint - (Vector2)Bounds.center;

			// prevent jittering
			Vector2 halfExtentsCombined = ((Vector2)Bounds.extents + (targetCollider == null || (m_pathfindWaypoints != null && m_pathfindWaypoints.Count > 1) ? Vector2.zero : targetCollider.bounds.extents)) * 0.5f;
			if (Mathf.Abs(waypointDiff.x) < halfExtentsCombined.x)
			{
				waypointDiff.x = 0.0f;
			}
			if (Mathf.Abs(waypointDiff.y) < halfExtentsCombined.y)
			{
				waypointDiff.y = 0.0f;
			}

			// check arrival
			const float arrivalEpsilon = 0.1f; // TODO: derive/calculate?
			bool isHighEnough = isStartingPoint || IsGrounded || HasFlying || transform.position.y >= waypoint.y; // NOTE the stricter condition when in mid-air to prevent starting to move sideways too soon and falling back below the waypoint
			return isHighEnough && waypointDiff.magnitude <= ((Vector2)Bounds.extents).magnitude + arrivalEpsilon;
		}

		if (m_target == null || ShouldSkipUpdates)
		{
			move = HasFlying || IsGrounded ? Vector2.zero : move;
			return false; // TODO: flag to trigger idle behavior if unable to find target?
		}

		// pathfind
		// TODO: efficiency?
		bool isStartingPoint = false;
		if (m_pathfindTimeNext <= Time.time || (m_pathfindWaypoints != null && m_pathfindWaypoints.Count > 0 && Vector2.Distance(m_target.transform.position, m_pathfindWaypoints.Last()) > targetOffsetAbs.magnitude + Mathf.Max(0.0f, m_meleeRange))) // TODO: better re-plan trigger(s) (more precise as distance remaining decreases); avoid trying to go past moving targets?
		{
			System.Tuple<List<Vector2>, float> path = Pathfind(gameObject, m_target.gameObject, targetOffsetAbs);
			m_pathfindWaypoints = path?.Item1;
			isStartingPoint = true;
			if (m_pathfindWaypoints == null)
			{
				m_target = null; // TODO: better handle unreachable positions; idle? find closest reachable position?
			}
			else if (!m_friendly && path.Item2 < m_alertDistanceMax && GameController.Instance.m_avatars.Any(avatar => avatar.gameObject == m_target.gameObject))
			{
				// if targeting and successfully pathfinding to a close-enough avatar, ensure we are now contained in GameController.m_enemiesInWave[]
				// TODO: efficiency? remove if later unable to pathfind again? activate when targeting NPCs (while still preventing softlocks)?
				GameController.Instance.EnemyAddToWave(this);
			}
			m_pathfindTimeNext = Time.time + Random.Range(m_replanSecondsMin, m_replanSecondsMax);
		}
		Collider2D targetCollider = m_target == null ? null : m_target.GetComponent<Collider2D>(); // TODO: efficiency?
		if (m_pathfindWaypoints == null || m_pathfindWaypoints.Count == 0)
		{
			// NOTE that even if targeting an avatar and unable to pathfind to any avatars, we don't have to remove ourself from GameController.m_enemiesInWave[] to prevent wave softlocks, since GameController.SpawnEnemyWaveCoroutine() takes care of that
			move = Vector2.zero;
			return m_target != null && isAtWaypoint(targetCollider == null ? m_target.transform.position : targetCollider.bounds.center, targetCollider, isStartingPoint);
		}

		// process & check for arrival at current waypoint(s)
		Vector2 nextWaypoint;
		bool atWaypoint;
		do
		{
			// get relative position
			nextWaypoint = m_pathfindWaypoints.First();
			atWaypoint = isAtWaypoint(nextWaypoint, targetCollider, isStartingPoint);
			if (atWaypoint)
			{
				m_pathfindWaypoints.RemoveAt(0);
				isStartingPoint = false;
			}
		}
		while (atWaypoint && m_pathfindWaypoints.Count > 0);

		// determine current direction
		Vector2 dir = waypointDiff.normalized;
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
			Bounds selfBounds = Bounds;
			List<ContactPoint2D> contacts = new();
			body.GetContacts(contacts);
			bool jumpOverObstruction = dir.y.FloatEqual(0.0f) && contacts.Any(c => Vector2.Dot(c.normal, dir) < 0.0f);
			if (IsGrounded && (m_jumpAlways || jumpOverObstruction || nextBounds.min.y > selfBounds.max.y) && Random.value <= (jumpOverObstruction || (dir.x == 0.0f && dir.y != 0.0f) ? m_jumpPctRapid : m_jumpPct))
			{
				m_jump = 1.0f;
				if (!m_airControl)
				{
					move.x *= m_jumpMaxSpeedOverride / maxSpeed;

					// determine how hard to jump
					float gravityEffective = -9.81f * gravityModifier; // TODO: determine why Physics2D.gravity is inaccurate; don't assume vertical gravity?
					float determinant = jumpTakeOffSpeed * jumpTakeOffSpeed - 4.0f * gravityEffective * -waypointDiff.y; // TODO: better determination of how far up/down we'll land if too far to make it to the waypoint?
					if (determinant < 0.0f)
					{
						determinant = jumpTakeOffSpeed * jumpTakeOffSpeed;
					}
					float jumpMaxDist = (-jumpTakeOffSpeed - Mathf.Sqrt(determinant)) / (2.0f * gravityEffective) * m_jumpMaxSpeedOverride;
					m_jump *= Mathf.Min(1.0f, waypointDiff.magnitude / jumpMaxDist); // TODO: separate x/y logic for improved upward movement?
				}
			}
			move.y = IsGrounded && nextBounds.max.y < selfBounds.min.y ? -1.0f : Mathf.SmoothDamp(move.y, 0.0f, ref m_dropDecayVel, m_dropDecayTime * 2.0f); // NOTE the IsGrounded check and damped decay (x2 since IsDropping's threshold is -0.5) to cause stopping at each ladder rung when descending // TODO: handle dropping through springs
		}

		return m_pathfindWaypoints.Count == 0;
	}

	private const float m_climbMinJumpSpeed = 10.0f; // TODO: calculate/derive?
	public System.Tuple<List<Vector2>, float> Pathfind(GameObject startObj, GameObject endObj, Vector2 targetOffsetAbs) => GameController.Instance.Pathfind(startObj, endObj, Bounds.extents.y, !HasFlying && jumpTakeOffSpeed < m_climbMinJumpSpeed ? Bounds.extents.y : float.MaxValue, targetOffsetAbs, RoomController.PathFlags.ObstructionCheck | (HasFlying ? RoomController.PathFlags.IgnoreGravity : RoomController.PathFlags.None)); // TODO: limit to max jump height once pathfinding takes platforms into account? prevent pathing beyond a threshold distance?

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
		m_aiState?.Exit();
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
			if (arm.GetComponentsInDirectChildren<IAttachable>(a => a.Component).Count() > 0)
			{
				continue;
			}
			arm.ChildAttach(GameController.Instance.m_savableFactory.Instantiate(m_heldItems.RandomWeighted().gameObject, transform.position, transform.rotation).GetComponent<ItemController>()); // TODO: generic SavableFactory.Instantiate() param?
		}

		// clear AI state in case we had just planned to go get items
		if (m_aiState != null)
		{
			m_aiState.Exit();
			m_aiState = null;
		}
	}

	private void UpdateAimPosition()
	{
		if (AimFreeze)
		{
			return;
		}

		// base target position
		m_aimPosition = m_target == null ? (Vector2)transform.position + ((!move.x.FloatEqual(0.0f) ? move.x < 0.0f : LeftFacing) ? Vector2.left : Vector2.right) : m_target.transform.position; // TODO: ensure that desired movement direction is available here? take waypoints into account?

		// aim directly if no arms/items
		ArmController[] arms = GetComponentsInChildren<ArmController>();
		if (arms.Length <= 0)
		{
			return;
		}
		ItemController aimItem = null;
		arms.FirstOrDefault(arm =>
		{
			aimItem = arm.GetComponentInChildren<ItemController>();
			return aimItem != null;
		});
		if (aimItem == null)
		{
			return;
		}

		// approximate the parabolic trajectory
		// given ax^2 + bx + c = 0, b = (-c - ax^2) / x = -c/x - ax
		Vector2 posDiff = m_aimPosition - (Vector2)aimItem.transform.position;
		float timeDiffApprox = posDiff.magnitude / aimItem.m_throwSpeed;
		if (timeDiffApprox > 0.0f)
		{
			const float gravity = /*Physics2D.gravity.y*/-9.81f; // TODO: determine why Physics2D.gravity does not match the outcome
			float launchSlopePerSec = posDiff.y / timeDiffApprox - gravity * timeDiffApprox;
			m_aimPosition.y = aimItem.transform.position.y + launchSlopePerSec * timeDiffApprox;
		}

		return;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void EnablePlayerControl()
	{
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void TeleportActivate() => TeleportTime = Time.time;

	[System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "called from animation event")]
	private void TeleportFinish() => TeleportTimeFinish = Time.time;
}
