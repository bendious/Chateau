using Platformer.Mechanics;
using UnityEngine;


public abstract class AIState
{
	public virtual void Enter(EnemyController ai) {}
	public abstract AIState Update(EnemyController ai);
	public virtual void Exit(EnemyController ai) {}
}


public sealed class AIPursue : AIState
{
	public Transform m_target;
	public float m_targetDistance;


	public static AIPursue FromAI(EnemyController ai)
	{
		return new AIPursue { m_target = ai.m_target, m_targetDistance = Random.value > 0.9f ? ai.m_meleeRange * 0.75f : ai.m_targetDistance }; // NOTE that even enemies w/ range go in for melee sometimes // TODO: EnemyController.disallowMelee flag?
	}

	public override AIState Update(EnemyController ai)
	{
		bool hasArrived = ai.NavigateTowardTarget(m_target, m_targetDistance);

		// check for ammo need
		if (ai.m_maxPickUps > 0 && ai.GetComponentInChildren<ItemController>() == null)
		{
			return new AIFindAmmo();
		}

		// check for arrival
		if (hasArrived)
		{
			return m_targetDistance > ai.m_meleeRange ? new AIThrow() : new AIMelee();
		}

		return null;
	}
}


public sealed class AIMelee : AIState
{
	private ItemController m_item;


	public override void Enter(EnemyController ai)
	{
		m_item = ai.GetComponentInChildren<ItemController>();
		m_item.Swing();
	}

	public override AIState Update(EnemyController ai)
	{
		if (m_item.Speed < m_item.m_damageThresholdSpeed)
		{
			return AIPursue.FromAI(ai);
		}

		return null;
	}
}


public sealed class AIThrow : AIState
{
	public float m_waitSeconds = 0.5f;


	private float m_startTime;


	public override void Enter(EnemyController ai)
	{
		ai.GetComponentInChildren<ItemController>().Throw();
		m_startTime = Time.time;
	}

	public override AIState Update(EnemyController ai)
	{
		if (Time.time >= m_startTime + m_waitSeconds)
		{
			return ai.GetComponentInChildren<ItemController>() == null ? new AIFindAmmo() : AIPursue.FromAI(ai);
		}
		return null;
	}
}


public sealed class AIFindAmmo : AIState
{
	private Transform m_target;


	public override void Enter(EnemyController ai)
	{
		m_target = FindTarget(ai);
	}

	public override AIState Update(EnemyController ai)
	{
		// validate target
		if (m_target == null || (m_target.parent != null && m_target.parent != ai.transform))
		{
			m_target = FindTarget(ai);
			if (m_target == null)
			{
				// no items anywhere? fallback on pursuit
				return AIPursue.FromAI(ai);
			}
		}

		// move
		bool hasArrived = ai.NavigateTowardTarget(m_target, 0.0f);

		// pick up target
		if (hasArrived)
		{
			m_target.GetComponent<ItemController>().AttachTo(ai.gameObject);
			return AIPursue.FromAI(ai);
		}

		return null;
	}


	private Transform FindTarget(EnemyController ai)
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
			// TODO: use pathfind distance? de-prioritize based on vertical distance / passing through ai.m_target?
			float distSq = ((Vector2)tf.position - (Vector2)ai.transform.position).sqrMagnitude;
			if (distSq < closestDistSq)
			{
				closest = tf;
				closestDistSq = distSq;
			}
		}

		return closest;
	}
}
