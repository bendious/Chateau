using UnityEngine;


public abstract class AIState
{
	protected readonly EnemyController m_ai;


	public AIState(EnemyController ai) => m_ai = ai;

	public virtual void Enter() {}
	public abstract AIState Update();
	public virtual AIState OnDamage(GameObject source) { return null; }
	public virtual void Exit() {}

#if UNITY_EDITOR
	public void DebugGizmo()
	{
		if (ConsoleCommands.AIDebugLevel >= (int)ConsoleCommands.AIDebugLevels.State)
		{
			UnityEditor.Handles.Label(m_ai.transform.position, ToString());
		}
	}
#endif
}


public sealed class AIPursue : AIState
{
	private readonly Vector2 m_targetOffset;


	public AIPursue(EnemyController ai)
		: base(ai)
	{
		m_targetOffset = Random.value > 0.9f ? 0.75f/*?*/ * m_ai.m_meleeRange * Vector2.right : m_ai.m_targetOffset; // NOTE that even enemies w/ range go in for melee sometimes // TODO: EnemyController.disallowMelee flag?
	}

	public override AIState Update()
	{
		bool hasArrived = m_ai.NavigateTowardTarget(m_targetOffset);

		// check for target death
		AvatarController targetAvatar = m_ai.m_target == null ? null : m_ai.m_target.GetComponent<AvatarController>();
		if (targetAvatar != null && !targetAvatar.controlEnabled)
		{
			m_ai.m_target = null;
			return null;
		}

		// check for ammo need
		bool hasItem = m_ai.GetComponentInChildren<ItemController>() != null;
		if (m_ai.MaxPickUps > 0 && !hasItem)
		{
			// TODO: prevent thrashing between AIPursue/AIFindAmmo when no ammo is reachable
			return new AIFindAmmo(m_ai);
		}

		// check for arrival
		if (hasArrived)
		{
			if (hasItem)
			{
				return m_targetOffset.magnitude > m_ai.m_meleeRange ? new AIThrow(m_ai) : new AIMelee(m_ai);
			}
			else
			{
				return new AIRamSwoop(m_ai);
			}
		}

		return null;
	}
}


public sealed class AIFlee : AIState
{
	public Vector2 m_fleeOffset = 12.0f * Vector2.right;


	public AIFlee(EnemyController ai)
		: base(ai)
	{
	}

	public override AIState Update()
	{
		m_ai.NavigateTowardTarget(m_fleeOffset);

		// check target availability
		AvatarController targetAvatar = m_ai.m_target.GetComponent<AvatarController>();
		if (targetAvatar == null || targetAvatar.controlEnabled)
		{
			return new AIPursue(m_ai);
		}

		return null;
	}
}


public sealed class AIMelee : AIState
{
	public float m_durationSeconds = 1.0f;
	public float m_swingTimeSeconds = 0.2f;


	private float m_startTime;
	private float m_swingTime;


	private ItemController m_item;


	public AIMelee(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_startTime = Time.time;

		m_item = m_ai.GetComponentInChildren<ItemController>();
		m_item.Swing();
	}

	public override AIState Update()
	{
		m_ai.NavigateTowardTarget(Vector2.zero);

		if (Time.time >= m_swingTime + m_swingTimeSeconds)
		{
			m_item.Swing();
			m_swingTime = Time.time;
		}

		if (Time.time >= m_startTime + m_durationSeconds)
		{
			return new AIPursue(m_ai);
		}

		return null;
	}
}


public sealed class AIThrow : AIState
{
	public float m_waitSeconds = 0.5f;


	private ItemController m_item;

	private float m_startTime = 0.0f;


	public AIThrow(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_item = m_ai.GetComponentInChildren<ItemController>();
	}

	public override AIState Update()
	{
		m_ai.move = Vector2.zero;

		if (m_startTime == 0.0f)
		{
			// pre-throw
			if (m_item.Speed < m_item.m_swingInfo.m_damageThresholdSpeed) // TODO: better aimReady flag?
			{
				m_item.Throw();
				m_startTime = Time.time;
			}
			return null;
		}

		// post-throw
		if (Time.time < m_startTime + m_waitSeconds)
		{
			return null;
		}

		// finished
		return m_ai.GetComponentInChildren<ItemController>() == null ? new AIFindAmmo(m_ai) : new AIPursue(m_ai);
	}
}


public sealed class AIRamSwoop : AIState
{
	public float m_speed = 8.0f; // TODO: build up speed in Update() based on angle


	private /*readonly*/ Vector2 m_startPosToTarget;
	private /*readonly*/ float m_speedOrig;
	private /*readonly*/ float m_radiansPerSecond;

	private float m_angleRadians = 0.0f;


	public AIRamSwoop(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_startPosToTarget = (Vector2)m_ai.m_target.position - (Vector2)m_ai.transform.position;

		m_speedOrig = m_ai.maxSpeed;
		m_ai.maxSpeed = m_speed;

		float travelDist = Mathf.PI * Mathf.Sqrt((m_startPosToTarget.x * m_startPosToTarget.x + m_startPosToTarget.y * m_startPosToTarget.y) * 0.5f); // perimeter of ellipse = 2pi*sqrt((a^2+b^2)/2), and we're traveling half that
		float durationSec = travelDist / m_speed;
		m_radiansPerSecond = Mathf.PI / durationSec;

		m_ai.PlayAttackEffects();
	}

	public override AIState Update()
	{
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

		return m_angleRadians >= Mathf.PI ? new AIPursue(m_ai) : null;
	}

	public override AIState OnDamage(GameObject source)
	{
		return new AIPursue(m_ai);
	}

	public override void Exit()
	{
		m_ai.transform.rotation = Quaternion.identity;

		m_ai.StopAttackEffects();

		m_ai.maxSpeed = m_speedOrig;
	}
}


public sealed class AIFindAmmo : AIState
{
	public float m_multiFindPct = 0.5f; // TODO: determine based on relative distances to other items versus m_ai.m_target?


	public AIFindAmmo(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_ai.m_target = FindTarget();
	}

	public override AIState Update()
	{
		// validate target
		// TODO: switch targets if a different one is now closer?
		if (m_ai.m_target == null || (m_ai.m_target.parent != null && m_ai.m_target.parent != m_ai.transform))
		{
			m_ai.m_target = FindTarget();
			if (m_ai.m_target == null)
			{
				// no items anywhere? fallback on pursuit
				// TODO: prevent thrashing between AIPursue/AIFindAmmo when no ammo is reachable
				return new AIPursue(m_ai);
			}
		}

		// move
		bool hasArrived = m_ai.NavigateTowardTarget(Vector2.zero);

		// TODO: time-out if taking too long, to prevent getting stuck?

		// pick up target
		if (hasArrived)
		{
			m_ai.AttachItem(m_ai.m_target.GetComponent<ItemController>());
			m_ai.m_target = null;
			if (m_ai.GetComponentsInChildren<ItemController>().Length >= m_ai.MaxPickUps || Random.value > m_multiFindPct)
			{
				return new AIPursue(m_ai);
			}
		}

		return null;
	}


	private Transform FindTarget()
	{
		// TODO: efficiency?
		GameObject[] items = GameObject.FindGameObjectsWithTag("Item");

		Transform closest = null;
		float closestDistSq = float.MaxValue;
		foreach (GameObject obj in items)
		{
			Transform tf = obj.transform;
			if (tf.parent != null)
			{
				continue; // ignore held items
			}

			// prioritize by pathfind distance
			// TODO: efficiency? also prioritize based on item damage? de-prioritize based on vertical distance / passing through m_ai.m_target? use RandomWeighted() to allow retries to bypass unreachable "closest" options?
			System.Collections.Generic.List<Vector2> path = GameController.Instance.Pathfind(m_ai.transform.position, tf.position, Vector2.zero);
			if (path == null)
			{
				continue; // ignore unreachable items
			}
			float distSq = 0.0f;
			for (int i = 0; i < path.Count - 1; ++i)
			{
				distSq += (path[i + 1] - path[i]).sqrMagnitude;
			}
			if (distSq < closestDistSq)
			{
				closest = tf;
				closestDistSq = distSq;
			}
		}

		return closest;
	}
}
