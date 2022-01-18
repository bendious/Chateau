using Platformer.Mechanics;
using UnityEngine;


public abstract class AIState
{
	protected readonly EnemyController m_ai;


	public AIState(EnemyController ai) => m_ai = ai;

	public virtual void Enter() {}
	public abstract AIState Update();
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
	private readonly Transform m_target;
	private readonly Vector2 m_targetOffset;


	public AIPursue(EnemyController ai)
		: base(ai)
	{
		m_target = m_ai.m_target;
		m_targetOffset = Random.value > 0.9f ? 0.75f/*?*/ * m_ai.m_meleeRange * Vector2.right : m_ai.m_targetOffset; // NOTE that even enemies w/ range go in for melee sometimes // TODO: EnemyController.disallowMelee flag?
	}

	public override AIState Update()
	{
		bool hasArrived = m_ai.NavigateTowardTarget(m_target, m_targetOffset);

		// check for target death
		AvatarController targetAvatar = m_target.GetComponent<AvatarController>();
		if (targetAvatar != null && !targetAvatar.controlEnabled)
		{
			return new AIFlee(m_ai);
		}

		// check for ammo need
		bool hasItem = m_ai.GetComponentInChildren<ItemController>() != null;
		if (m_ai.m_maxPickUps > 0 && !hasItem)
		{
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


	private readonly Transform m_target;


	public AIFlee(EnemyController ai)
		: base(ai)
	{
		m_target = m_ai.m_target;
	}

	public override AIState Update()
	{
		m_ai.NavigateTowardTarget(m_target, m_fleeOffset);

		// check target availability
		AvatarController targetAvatar = m_target.GetComponent<AvatarController>();
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
		m_ai.NavigateTowardTarget(m_ai.m_target, Vector2.zero);

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
		if (m_startTime == 0.0f)
		{
			// pre-throw
			if (m_item.Speed < m_item.m_damageThresholdSpeed) // TODO: better aimReady flag?
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
	public float m_durationSeconds = 1.0f;


	private /*readonly*/ Vector2 m_targetingScalars;
	private /*readonly*/ float m_speedScalar;
	private /*readonly*/ float m_degreesPerSecond;

	private float m_angleDegrees = 0.0f;


	public AIRamSwoop(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_targetingScalars = (Vector2)m_ai.transform.position - (Vector2)m_ai.m_target.position; // this stretches circular movement into an ellipse based on the target offset

		m_speedScalar = Mathf.PI * 0.5f * Mathf.Max(Mathf.Abs(m_targetingScalars.x), Mathf.Abs(m_targetingScalars.y)) / m_durationSeconds; // this scales the movement speed in order to move through one-quarter of a circle in the allotted time
		m_ai.maxSpeed *= m_speedScalar;

		m_degreesPerSecond = 180.0f / m_durationSeconds;

		// TODO: trigger animation/SFX
	}

	public override AIState Update()
	{
		m_ai.move = Quaternion.AngleAxis(m_angleDegrees, Vector3.back) * Vector2.down * m_targetingScalars;

		m_angleDegrees += m_degreesPerSecond * Time.deltaTime;

		return m_angleDegrees >= 180.0f ? new AIPursue(m_ai) : null;
	}

	public override void Exit()
	{
		m_ai.maxSpeed /= m_speedScalar;
	}
}


public sealed class AIFindAmmo : AIState
{
	private Transform m_target;


	public AIFindAmmo(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_target = FindTarget();
	}

	public override AIState Update()
	{
		// validate target
		if (m_target == null || (m_target.parent != null && m_target.parent != m_ai.transform))
		{
			m_target = FindTarget();
			if (m_target == null)
			{
				// no items anywhere? fallback on pursuit
				return new AIPursue(m_ai);
			}
		}

		// move
		bool hasArrived = m_ai.NavigateTowardTarget(m_target, Vector2.zero);

		// pick up target
		if (hasArrived)
		{
			m_target.GetComponent<ItemController>().AttachTo(m_ai.gameObject);
			return new AIPursue(m_ai);
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

			// prioritize by distance
			// TODO: use pathfind distance? de-prioritize based on vertical distance / passing through m_ai.m_target?
			float distSq = ((Vector2)tf.position - (Vector2)m_ai.transform.position).sqrMagnitude;
			if (distSq < closestDistSq)
			{
				closest = tf;
				closestDistSq = distSq;
			}
		}

		return closest;
	}
}
