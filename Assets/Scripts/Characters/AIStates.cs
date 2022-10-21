using System.Linq;
using UnityEngine;


public abstract class AIState
{
	public enum Type
	{
		Fraternize,
		Pursue,
		PursueErratic,
		Flee,
		RamSwoop,

		Melee,
		Throw,
		ThrowAll,
		ThrowAllNarrow,
		FindAmmo,

		Teleport,
		Spawn,
		FinalDialogue,
	};


	protected readonly AIController m_ai;


	private readonly bool m_targetFriendlies;
	private readonly float m_ignoreDistancePct;


	public AIState(AIController ai, bool targetFriendlies = false, float ignoreDistancePct = 0.0f)
	{
		m_ai = ai;
		m_targetFriendlies = targetFriendlies;
		m_ignoreDistancePct = ignoreDistancePct;
	}

	public static AIState FromTypePrioritized(Type[] allowedTypes, AIController ai)
	{
		float distanceFromTarget = ai.m_target == null ? float.MaxValue : Vector2.Distance(ai.transform.position, ai.m_target.transform.position); // TODO: use closest bbox points?
		Vector2 targetOffset = ai.m_target == null ? Vector2.zero : ai.m_target.transform.position.x < ai.transform.position.x ? ai.m_targetOffset : new(-ai.m_targetOffset.x, ai.m_targetOffset.y); // TODO: use ai.m_meleeRange if going for a melee? account for item size when meleeing w/ item?
		float distanceFromOffsetPos = ai.m_target == null ? float.MaxValue : Vector2.Distance(ai.transform.position, (Vector2)ai.m_target.transform.position + targetOffset);
		int numItems = ai.GetComponentsInChildren<ItemController>().Length;
		int holdCountMax = ai.HoldCountMax;
		float itemHoldPct = (float)numItems / System.Math.Max(1, holdCountMax);

		// TODO: efficiency?
		bool enemyAccessible = GameController.Instance.AiTargets.Any(target => target.TargetPriority(ai, false) > 0.0f && PathfindDistanceTo(ai, target.gameObject) != float.MaxValue);
		bool itemAccessible = allowedTypes.Contains(Type.FindAmmo) && ItemsTargetable.Any(item => item.transform.parent == null && PathfindDistanceTo(ai, item.gameObject) != float.MaxValue);

		float[] priorities = allowedTypes.Select(type =>
		{
			switch (type)
			{
				// TODO: more granular priorities?
				case Type.Fraternize:
					return enemyAccessible ? 0.0f : float.Epsilon;
				case Type.Pursue:
				case Type.PursueErratic:
					return enemyAccessible && distanceFromOffsetPos > ai.m_meleeRange && (numItems > 0 || holdCountMax <= 0 || !itemAccessible) ? (distanceFromOffsetPos != float.MaxValue ? 1.0f : float.Epsilon) : 0.0f;
				case Type.Flee:
					return !ai.m_friendly && GameController.Instance.Victory ? 100.0f : 0.0f;
				case Type.Melee:
					return (numItems > 0 || !itemAccessible || !allowedTypes.Contains(Type.FindAmmo)) && holdCountMax > 0 ? Mathf.InverseLerp(2.0f * ai.m_meleeRange, ai.m_meleeRange, distanceFromTarget) : 0.0f;
				case Type.Throw:
					return ai.m_target != null && distanceFromTarget > ai.m_meleeRange && numItems > 0 ? 1.0f : 0.0f;
				case Type.ThrowAll:
					return ai.m_target != null && numItems > 1 ? itemHoldPct : 0.0f;
				case Type.ThrowAllNarrow:
					return ai.m_target != null && numItems > 1 ? itemHoldPct : 0.0f;
				case Type.RamSwoop:
					return distanceFromOffsetPos <= ai.m_meleeRange + ai.GetComponent<Collider2D>().bounds.extents.magnitude ? 1.0f : 0.0f; // TODO: better conditions?
				case Type.FindAmmo:
					return !itemAccessible || holdCountMax <= 0 ? 0.0f : 1.0f - itemHoldPct;
				case Type.Teleport:
					return Mathf.Max(0.0f, AITeleport.CooldownPct * (1.0f - 1.0f / distanceFromOffsetPos));
				case Type.Spawn:
					return ai.m_attackPrefabs.Length > 0 && ai.m_target != null && ai.m_target.GetComponent<Health>().IsAlive ? 1.0f : 0.0f;
				case Type.FinalDialogue:
					return ai.m_target != null && ai.m_target.GetComponent<Health>().CurrentHP <= 1.0f && !ai.GetComponent<Health>().CanIncrement ? float.MaxValue : 0.0f;
				default:
					Debug.Assert(false, "Unhandled AIState.");
					return 0.0f;
			}
		}).ToArray();

		switch (allowedTypes.RandomWeighted(priorities))
		{
			case Type.Fraternize:
				return new AIFraternize(ai);
			case Type.Pursue:
				return new AIPursue(ai);
			case Type.PursueErratic:
				return new AIPursueErratic(ai);
			case Type.Flee:
				return new AIFlee(ai);
			case Type.Melee:
				return new AIMelee(ai);
			case Type.Throw:
				return new AIThrow(ai);
			case Type.ThrowAll:
				return new AIThrowAll(ai);
			case Type.ThrowAllNarrow:
				return new AIThrowAllNarrow(ai);
			case Type.RamSwoop:
				return new AIRamSwoop(ai);
			case Type.FindAmmo:
				return new AIFindAmmo(ai);
			case Type.Teleport:
				return new AITeleport(ai);
			case Type.Spawn:
				AISpawn state = new(ai);

				// TODO: parameterize / move elsewhere?
				float healthPct = ai.GetComponent<Health>().PercentHP;
				if (healthPct < 0.5f)
				{
					state.m_waitForDespawn = false;
				}
				if (healthPct < 0.25f)
				{
					state.m_delaySeconds *= 0.5f;
				}

				return state;
			case Type.FinalDialogue:
				return new AIFinalDialogue(ai);
			default:
				Debug.Assert(false, "Unhandled AIState.");
				return null;
		}
	}

	public virtual void Enter()
	{
		// NOTE that we don't want all AI spawned at the same time to retarget/repath immediately, but we also don't want any to just stand around for too long, so we just use a small fixed randomization
		m_ai.m_targetSelectTimeNext = Time.time + Random.Range(0.0f, 0.5f);
		m_ai.m_pathfindTimeNext = m_ai.m_targetSelectTimeNext + Random.Range(0.0f, 0.5f);
	}

	public virtual AIState Update()
	{
		if (m_ai.m_targetSelectTimeNext <= Time.time)
		{
			Retarget();
			if (m_ai.m_target == null)
			{
				return null;
			}
			m_ai.m_targetSelectTimeNext = Time.time + Random.Range(m_ai.m_replanSecondsMin, m_ai.m_replanSecondsMax);
		}

		return this;
	}

	public virtual void Retarget()
	{
		KinematicCharacter[] candidates = GameController.Instance.AiTargets.ToArray();
		if (candidates.Length <= 0)
		{
			return;
		}
		System.Tuple<KinematicCharacter, System.Tuple<float, float>> targetBest = candidates.SelectMinWithValue(candidate =>
		{
			float priority = candidate.TargetPriority(m_ai, m_targetFriendlies);
			float dist = priority <= 0.0f ? float.MaxValue : Random.value < m_ignoreDistancePct ? Random.Range(0.0f, float.MaxValue) : PathfindDistanceTo(m_ai, candidate.gameObject); // OPTIMIZATION: skip pathfinding if totally excluded by priority
			return System.Tuple.Create(priority, dist);
		}, new PriorityDistanceComparer());
		if (targetBest.Item2.Item1 > 0.0f)
		{
			m_ai.m_target = targetBest.Item1;
		}
	}

	public virtual AIState OnDamage(GameObject source, float amountUnscaled) => amountUnscaled >= m_ai.GetComponent<Health>().m_minorDamageThreshold ? null : this;
	public virtual void Exit() => m_ai.ClearPath(true);

#if UNITY_EDITOR
	public void DebugGizmo()
	{
		if (ConsoleCommands.AIDebugLevel >= (int)ConsoleCommands.AIDebugLevels.State)
		{
			UnityEditor.Handles.Label(m_ai.transform.position, ToString());
		}
	}
#endif


	protected static ItemController[] ItemsTargetable => GameObject.FindObjectsOfType<ItemController>(); // TODO: efficiency? include backpacks as well as items?

	protected static float PathfindDistanceTo(AIController ai, GameObject obj)
	{
		// TODO: efficiency? also prioritize based on damage? de-prioritize based on vertical distance / passing through m_ai.m_target?
		System.Tuple<System.Collections.Generic.List<Vector2>, float> path = GameController.Instance.Pathfind(ai.gameObject, obj, ai.GetComponent<Collider2D>().bounds.extents.y, !ai.HasFlying && ai.jumpTakeOffSpeed <= 0.0f ? 0.0f : float.MaxValue); // TODO: limit to max jump height once pathfinding takes platforms into account?
		return path == null ? float.MaxValue : path.Item2;
	}
}


public sealed class AIFraternize : AIState
{
	// since we ignore distance when retargeting, we have to prevent thrashing too often
	public float m_retargetSecMin = 15.0f;
	public float m_retargetSecMax = 30.0f;

	public float m_postSecMin = 1.0f;
	public float m_postSecMax = 2.0f;

	public float m_enemyCheckSecMin = 1.0f;
	public float m_enemyCheckSecMax = 2.0f;


	private readonly float m_retargetOrigMin;
	private readonly float m_retargetOrigMax;

	private float m_postSecRemaining;

	private float m_enemyCheckTime;


	public AIFraternize(AIController ai)
		: base(ai, true, 1.0f)
	{
		m_retargetOrigMin = m_ai.m_replanSecondsMin;
		m_retargetOrigMax = m_ai.m_replanSecondsMax;

		ResetEnemyCheckTime();
	}

	public override void Enter()
	{
		base.Enter();

		m_postSecRemaining = Random.Range(m_postSecMin, m_postSecMax);

		m_ai.m_replanSecondsMin = m_retargetSecMin;
		m_ai.m_replanSecondsMax = m_retargetSecMax;
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		bool hasArrived = m_ai.NavigateTowardTarget(Vector2.right * m_ai.m_meleeRange);

		// check for arrival
		if (hasArrived)
		{
			m_postSecRemaining -= Time.deltaTime;
			if (m_postSecRemaining <= 0.0f)
			{
				return null;
			}
		}

		// check for higher-priority goal becoming available/reachable
		if (m_enemyCheckTime <= Time.time)
		{
			if (GameController.Instance.AiTargets.Any(target => target.TargetPriority(m_ai, false) > 0.0f && PathfindDistanceTo(m_ai, target.gameObject) != float.MaxValue))
			{
				return null;
			}
			ResetEnemyCheckTime();
		}

		return this;
	}

	public override void Exit()
	{
		base.Exit();

		m_ai.m_replanSecondsMin = m_retargetOrigMin;
		m_ai.m_replanSecondsMax = m_retargetOrigMax;
		m_ai.m_target = null; // to prevent subsequent states aiming at / attacking friendlies
	}


	private void ResetEnemyCheckTime() => m_enemyCheckTime = Time.time + Random.Range(m_enemyCheckSecMin, m_enemyCheckSecMax);
}


public class AIPursue : AIState
{
	protected Vector2 m_targetOffset;


	public AIPursue(AIController ai)
		: base(ai)
	{
		m_targetOffset = m_ai.m_meleeRange >= 0.0f && ((ai.HoldCountMax > 0 && ai.GetComponentInChildren<ItemController>() == null) || Random.value > 0.9f) ? 0.75f/*?*/ * m_ai.m_meleeRange * Vector2.right : m_ai.m_targetOffset; // NOTE that even enemies w/ range go in for melee sometimes unless they are flagged w/ negative m_meleeRange
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		bool hasArrived = m_ai.NavigateTowardTarget(m_targetOffset);

		// check for target death
		AvatarController targetAvatar = m_ai.m_target == null ? null : m_ai.m_target.GetComponent<AvatarController>();
		if (targetAvatar != null && !targetAvatar.IsAlive)
		{
			m_ai.m_target = null;
			return null;
		}

		// check for arrival
		if (hasArrived)
		{
			return null;
		}

		return this;
	}

	public override void Retarget()
	{
		if (m_ai.OnlyPursueAvatar)
		{
			m_ai.m_target = GameController.Instance.m_avatars.Count <= 0 ? null : GameController.Instance.m_avatars.Random(); // TODO: choose nearest to prevent thrashing in co-op?
			return;
		}
		base.Retarget();
	}
}


public sealed class AIPursueErratic : AIPursue
{
	public float m_turnPct = 0.1f;

	public float m_postSecondsMin = 0.1f;
	public float m_postSecondsMax = 1.0f;


	private bool m_hasArrived = false;
	private float m_postSecondsRemaining;


	public AIPursueErratic(AIController ai)
		: base(ai)
	{
		RandomizeTargetOffset();
	}

	public override AIState Update()
	{
		if (Random.value <= m_turnPct)
		{
			RandomizeTargetOffset();
		}

		if (!m_hasArrived)
		{
			AIState baseRetVal = base.Update();
			if (baseRetVal == null)
			{
				m_hasArrived = true;
				m_postSecondsRemaining = Random.Range(m_postSecondsMin, m_postSecondsMax);
				return this;
			}
			return baseRetVal;
		}

		m_ai.move = Vector2.zero;
		if (m_postSecondsRemaining > 0.0f)
		{
			m_postSecondsRemaining -= Time.deltaTime;
			return this;
		}
		return null;
	}


	private void RandomizeTargetOffset() => m_targetOffset = Quaternion.Euler(0.0f, 0.0f, Random.Range(-90.0f, 90.0f)) * Vector2.up * m_targetOffset.magnitude;
}


public sealed class AIFlee : AIState
{
	public Vector2 m_fleeOffset = 12.0f * Vector2.right;


	public AIFlee(AIController ai)
		: base(ai)
	{
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		m_ai.NavigateTowardTarget(m_fleeOffset);

		// NOTE that once fleeing begins, it never ends except via base.Update() (e.g. unable to re-target)
		return this;
	}
}


public sealed class AIMelee : AIState
{
	public float m_swingTimeSeconds = 0.2f; // TODO: vary based on item weight?


	private float m_durationRemaining;
	private float m_swingDurationRemaining;


	private ItemController m_item;
	private ArmController m_arm;


	public AIMelee(AIController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		base.Enter();

		m_durationRemaining = Random.Range(m_ai.m_meleeSecondsMin, m_ai.m_meleeSecondsMax);

		m_item = m_ai.GetComponentInChildren<ItemController>();
		m_arm = m_item != null ? m_item.GetComponentInParent<ArmController>() : m_ai.GetComponentInChildren<ArmController>(); // NOTE that we get the relevant arm even if planning to use m_item, since m_item could break or be taken during the course of this state // TODO: don't assume that AI only have items via arms?
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		m_ai.NavigateTowardTarget(Vector2.zero);

		m_swingDurationRemaining -= Time.deltaTime;
		if (m_swingDurationRemaining <= 0.0f)
		{
			Swing();
		}

		m_durationRemaining -= Time.deltaTime;
		if (m_durationRemaining <= 0.0f)
		{
			return null;
		}

		return this;
	}


	private void Swing()
	{
		if (m_item != null && m_item.transform.parent == m_arm.transform) // NOTE that we have to check whether we're still holding the item since it could have broken or been taken at any point since Enter()
		{
			m_item.Swing(false);
		}
		else
		{
			m_arm.Swing(false, Random.value < 0.5f); // TODO: more deliberate use of jabs/swings?
		}
		m_swingDurationRemaining = m_swingTimeSeconds;
	}
}


public class AIThrow : AIState
{
	public float m_waitSeconds = 0.5f;


	// NOTE the use of relative rather than absolute times to avoid jumping to the end if started while passive
	protected float PreSecondsRemaining { get; private set; }
	private float m_postSecondsRemaining;

	protected bool m_hasThrown = false;


	public AIThrow(AIController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		base.Enter();

		PreSecondsRemaining = m_waitSeconds;
		m_postSecondsRemaining = m_waitSeconds;
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		m_ai.move = Vector2.zero;

		// pre-throw
		if (PreSecondsRemaining > 0.0f)
		{
			PreSecondsRemaining -= Time.deltaTime;
			return this;
		}

		// aim/throw
		if (!m_hasThrown)
		{
			ItemController item = m_ai.GetComponentInChildren<ItemController>(); // NOTE that we can't cache this since it's possible for it to be snatched away between frames
			if (item != null && item.Speed < item.m_swingInfo.m_damageThresholdSpeed) // NOTE that if all items have been snatched we could return null in order to immediately cancel, but we instead keep going, as if confused, to reward players for snatching AI items // TODO: better aimReady flag?
			{
				item.Throw();
				m_hasThrown = true;
			}
			// NOTE that we go ahead and start decrementing m_postSecondsRemaining in order to avoid getting stuck if there are no items left or our aim never steadies
		}

		// post-throw
		if (m_postSecondsRemaining > 0.0f)
		{
			m_postSecondsRemaining -= Time.deltaTime;
			return this;
		}

		// finished
		return null;
	}
}


public class AIThrowAll : AIThrow
{
	public float m_waitSecondsOverride = 1.0f;


	protected bool m_hasThrownAll = false;


	private readonly float m_spinScalar;


	public AIThrowAll(AIController ai)
		: base(ai)
	{
		m_waitSeconds = m_waitSecondsOverride;
		m_spinScalar = 360.0f / m_waitSeconds; // TODO: move to Update() if m_waitSeconds ever needs to be set externally
	}

	public override AIState Update()
	{
		// arm spin during pre-throw
		m_ai.AimOffsetDegrees = !m_hasThrown ? (m_waitSeconds - PreSecondsRemaining) * m_spinScalar : 0.0f;

		AIState retVal = base.Update();

		if (m_hasThrown && !m_hasThrownAll)
		{
			foreach (ItemController item in m_ai.GetComponentsInChildren<ItemController>())
			{
				item.Throw();
			}
			m_hasThrownAll = true;
		}

		return retVal;
	}

	public override void Exit()
	{
		base.Exit();
		m_ai.AimOffsetDegrees = 0.0f; // in case of canceling during pre-throw
	}
}


public sealed class AIThrowAllNarrow : AIThrowAll
{
	public float m_narrowingPct = 0.25f;


	private readonly float m_narrowingScalar;


	public AIThrowAllNarrow(AIController ai)
		: base(ai)
	{
		m_narrowingScalar = 1.0f / m_waitSeconds; // TODO: move to Update() if m_waitSeconds ever needs to be set externally
	}

	public override AIState Update()
	{
		// arm narrowing during pre-throw
		m_ai.AimScalar = !m_hasThrown ? Mathf.Lerp(1.0f, m_narrowingPct, (m_waitSeconds - PreSecondsRemaining) * m_narrowingScalar) : 1.0f;

		AIState retVal = base.Update();

		m_ai.AimOffsetDegrees = 0.0f; // negate pre-throw spin from AIThrowAll

		return retVal;
	}

	public override void Exit()
	{
		base.Exit();
		m_ai.AimScalar = 1.0f; // in case of canceling during pre-throw
	}
}


public sealed class AIRamSwoop : AIState
{
	public float m_speed = 8.0f; // TODO: build up speed in Update() based on angle


	private /*readonly*/ Vector2 m_startPosToTarget;
	private /*readonly*/ float m_speedOrig;
	private /*readonly*/ float m_radiansPerSecond;

	private float m_angleRadians = 0.0f;


	public AIRamSwoop(AIController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		base.Enter();

		m_startPosToTarget = (Vector2)m_ai.m_target.transform.position - (Vector2)m_ai.transform.position;

		m_speedOrig = m_ai.maxSpeed;
		m_ai.maxSpeed = m_speed;

		float travelDist = Mathf.PI * Mathf.Sqrt((m_startPosToTarget.x * m_startPosToTarget.x + m_startPosToTarget.y * m_startPosToTarget.y) * 0.5f); // perimeter of ellipse = 2pi*sqrt((a^2+b^2)/2), and we're traveling half that
		float durationSec = travelDist / m_speed;
		m_radiansPerSecond = Mathf.PI / durationSec;

		m_ai.PlayAttackEffects();
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		// given m_startPosToTarget == (a,b),
		// we want position equation: (x,y) = (a - a*cos(theta), b*sin(theta))
		// so, the velocity equation is the derivative: (x',y') = (a*sin(theta), b*cos(theta))
		// this then needs to be normalized to scale to exactly m_ai.maxSpeed later
		m_ai.move = (m_startPosToTarget * new Vector2(Mathf.Sin(m_angleRadians), Mathf.Cos(m_angleRadians))).normalized;

		if (m_ai.HasFlying)
		{
			m_ai.transform.rotation = Quaternion.AngleAxis(Mathf.Rad2Deg * m_angleRadians * Mathf.Sign(m_startPosToTarget.x), Vector3.forward);
		}
		else
		{
			m_ai.move.y = 0.0f;
		}

		m_angleRadians += m_radiansPerSecond * Time.deltaTime;

		return m_angleRadians >= Mathf.PI ? null : this;
	}

	public override void Exit()
	{
		base.Exit();

		m_ai.transform.rotation = Quaternion.identity;

		m_ai.StopAttackEffects();

		m_ai.maxSpeed = m_speedOrig;
	}
}


public sealed class AIFindAmmo : AIState
{
	public float m_multiFindPct = 0.5f; // TODO: determine based on relative distances to other items versus m_ai.m_target?


	public AIFindAmmo(AIController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		base.Enter();

		Retarget();
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		// validate target
		if (m_ai.m_target == null || (m_ai.m_target.transform.parent != null && m_ai.m_target.transform.parent != m_ai.transform))
		{
			Retarget();
			if (m_ai.m_target == null)
			{
				// no reachable items? exit and fallback on some other state
				return null;
			}
		}

		// move
		bool hasArrived = m_ai.NavigateTowardTarget(Vector2.zero);

		// TODO: time-out if taking too long, to prevent getting stuck?

		// pick up target
		if (hasArrived)
		{
			m_ai.ChildAttach(m_ai.m_target.GetComponent<ItemController>());
			m_ai.m_target = null;
			if (m_ai.GetComponentsInChildren<ItemController>().Length >= m_ai.HoldCountMax || Random.value > m_multiFindPct)
			{
				return null;
			}
		}

		return this;
	}

	public override void Retarget()
	{
		ItemController[] items = ItemsTargetable;

		// prioritize by pathfind distance
		System.Tuple<ItemController, float> closestTarget = items.Length <= 0 ? System.Tuple.Create<ItemController, float>(null, float.MaxValue) : items.SelectMinWithValue(item =>
		{
			if (item.transform.parent != null)
			{
				return float.MaxValue; // ignore held items
			}
			return PathfindDistanceTo(m_ai, item.gameObject);
		});

		m_ai.m_target = closestTarget.Item1 == null || closestTarget.Item2 == float.MaxValue ? null : closestTarget.Item1;
	}

	public override void Exit()
	{
		base.Exit();
		m_ai.m_target = null; // to prevent subsequent states aiming at / attacking items
	}
}


public sealed class AITeleport : AIState
{
	public float m_delaySeconds = 0.5f;


	public static float CooldownPct => Time.time < m_lastFinishTime ? 0.0f : (Time.time - m_lastFinishTime) / (Time.time - m_lastFinishTime + m_cooldownHalflifeSeconds);


	private readonly float m_preDelayTime;
	private readonly float m_midDelayTime;
	private readonly float m_postDelayTime;

	private bool m_preVFXSpawned;
	private bool m_teleported;


	private const float m_cooldownHalflifeSeconds = 1.0f;
	private static float m_lastFinishTime;


	public AITeleport(AIController ai) : base(ai)
	{
		m_preDelayTime = Time.time + m_delaySeconds;
		m_midDelayTime = Time.time + m_delaySeconds * 2.0f;
		m_postDelayTime = Time.time + m_delaySeconds * 3.0f;

		m_lastFinishTime = m_postDelayTime;
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		m_ai.move = Vector2.zero;

		// TODO: animation

		if (m_preDelayTime > Time.time)
		{
			return this;
		}

		if (!m_preVFXSpawned)
		{
			if (m_ai.m_teleportVFX.Length > 0)
			{
				Object.Instantiate(m_ai.m_teleportVFX.RandomWeighted(), m_ai.transform.position, m_ai.transform.rotation);
			}
			m_preVFXSpawned = true;
		}

		if (m_midDelayTime > Time.time)
		{
			return this;
		}

		if (!m_teleported)
		{
			Vector3 newPos = GameController.Instance.RoomFromPosition(m_ai.transform.position).InteriorPosition(float.MaxValue, m_ai.gameObject) + (Vector3)m_ai.gameObject.OriginToCenterY();
			if (m_ai.m_teleportVFX.Length > 0)
			{
				Object.Instantiate(m_ai.m_teleportVFX.RandomWeighted(), newPos, m_ai.transform.rotation);
			}
			m_ai.Teleport(newPos);
			m_teleported = true;
		}

		if (m_postDelayTime > Time.time)
		{
			return this;
		}

		return null;
	}

	public override void Retarget() => m_ai.m_target = null;
}


public sealed class AISpawn : AIState
{
	public float m_delaySeconds = 0.5f;

	public bool m_waitForDespawn = true;


	private float m_preDelayRemaining;
	private float m_postDelayRemaining;

	private bool m_spawned;
	private GameObject m_spawnedObj;


	public AISpawn(AIController ai) : base(ai)
	{
	}

	public override void Enter()
	{
		base.Enter();
		m_preDelayRemaining = m_delaySeconds;
		m_postDelayRemaining = m_delaySeconds;
	}

	public override AIState Update()
	{
		AIState baseVal = base.Update();
		if (baseVal != this)
		{
			return baseVal;
		}

		m_ai.move = Vector2.zero;

		// TODO: animation?

		m_preDelayRemaining -= Time.deltaTime;
		if (m_preDelayRemaining > 0.0f)
		{
			return this;
		}

		if (!m_spawned)
		{
			Vector3 spawnPos = m_ai.m_target.transform.position + (Vector3)m_ai.m_attackOffset;
			m_spawnedObj = Object.Instantiate(m_ai.m_attackPrefabs.RandomWeighted(), spawnPos, Quaternion.identity);
			SpriteRenderer r = m_spawnedObj.GetComponent<SpriteRenderer>();
			if (r != null)
			{
				r.color = m_ai.GetComponent<SpriteRenderer>().color;
				r.flipX = spawnPos.x < m_ai.transform.position.x; // TODO: parameterize?
			}
			DespawnEffect despawnEffect = m_spawnedObj.GetComponent<DespawnEffect>();
			if (despawnEffect != null)
			{
				despawnEffect.CauseExternal = m_ai;
			}
			m_spawned = true;
		}

		if (m_waitForDespawn && m_spawnedObj != null)
		{
			return this;
		}

		m_postDelayRemaining -= Time.deltaTime;
		if (m_postDelayRemaining > 0.0f)
		{
			return this;
		}

		return null;
	}
}


public sealed class AIFinalDialogue : AIState
{
	public float m_musicFadeOutSeconds = 2.0f;


	private bool m_dialogueDone = false;


	public AIFinalDialogue(AIController ai) : base(ai)
	{
	}

	public override void Enter()
	{
		// NOTE that we deliberately don't invoke base.Enter() since we don't want the usual retargeting behavior

		GameController.Instance.GetComponent<MusicManager>().FadeOut(m_musicFadeOutSeconds);

		Boss boss = m_ai.GetComponent<Boss>();
		Dialogue dialogue = boss.m_dialogueFinal;
		Coroutine dialogueCoroutine = GameController.Instance.m_dialogueController.Play(dialogue.m_dialogue.RandomWeighted().m_lines, m_ai.gameObject, m_ai.m_target.GetComponent<AvatarController>(), boss.m_dialogueSprite, boss.GetComponent<SpriteRenderer>().color, dialogue.m_expressions, boss.m_dialogueSfx.RandomWeighted());
		m_ai.StartCoroutine(WaitForDialogue(dialogueCoroutine)); // TODO: ensure AIController never accidentally interferes via StopAllCoroutines()?
	}

	public override AIState Update()
	{
		// NOTE that we deliberately don't invoke base.Update() since we don't want the usual retargeting behavior

		if (!m_dialogueDone)
		{
			return this;
		}

		m_ai.GetComponent<Animator>().SetBool("dead", true);
		foreach (AvatarController avatar in GameController.Instance.m_avatars)
		{
			avatar.GetComponent<Animator>().SetTrigger("despawn");
		}

		return this; // NOTE that we never exit until character despawn
	}

	public override void Exit()
	{
		// NOTE that we deliberately don't invoke base.Exit() since this class ignores the standard functionality

		// despawn avatars since Credits uses its own controls
		foreach (AvatarController avatar in GameController.Instance.m_avatars)
		{
			Simulation.Schedule<ObjectDespawn>().m_object = avatar.gameObject;
		}

		UnityEngine.SceneManagement.SceneManager.LoadScene("Credits"); // TODO: un-hardcode?
	}


	private System.Collections.IEnumerator WaitForDialogue(Coroutine dialogueCoroutine)
	{
		yield return dialogueCoroutine;
		m_dialogueDone = true;
	}
}
