using Platformer.Mechanics;
using UnityEngine;


public abstract class AIState
{
	protected readonly EnemyController m_ai;


	public AIState(EnemyController ai) => m_ai = ai;

	public virtual void Enter() {}
	public abstract AIState Update();
	public virtual void Exit() {}
}


public sealed class AIPursue : AIState
{
	private readonly Transform m_target;
	private readonly float m_targetDistance;


	public AIPursue(EnemyController ai)
		: base(ai)
	{
		m_target = m_ai.m_target;
		m_targetDistance = Random.value > 0.9f ? m_ai.m_meleeRange * 0.75f : m_ai.m_targetDistance; // NOTE that even enemies w/ range go in for melee sometimes // TODO: EnemyController.disallowMelee flag?
	}

	public override AIState Update()
	{
		bool hasArrived = m_ai.NavigateTowardTarget(m_target, m_targetDistance);

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
		if (hasArrived && hasItem)
		{
			return m_targetDistance > m_ai.m_meleeRange ? new AIThrow(m_ai) : new AIMelee(m_ai);
		}

		return null;
	}
}


public sealed class AIFlee : AIState
{
	public float m_fleeDistance = 12.0f;


	private readonly Transform m_target;


	public AIFlee(EnemyController ai)
		: base(ai)
	{
		m_target = m_ai.m_target;
	}

	public override AIState Update()
	{
		m_ai.NavigateTowardTarget(m_target, m_fleeDistance);

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
	private ItemController m_item;


	public AIMelee(EnemyController ai)
		: base(ai)
	{
	}

	public override void Enter()
	{
		m_item = m_ai.GetComponentInChildren<ItemController>();
		m_item.Swing();
	}

	public override AIState Update()
	{
		if (m_item.Speed < m_item.m_damageThresholdSpeed)
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
		bool hasArrived = m_ai.NavigateTowardTarget(m_target, 0.0f);

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
