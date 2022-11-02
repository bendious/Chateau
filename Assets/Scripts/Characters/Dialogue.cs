using System;
using UnityEngine;


[CreateAssetMenu]
public class Dialogue : ScriptableObject
{
	[Serializable] public class Info
	{
		public string m_preconditionName;
		public int m_userdata;
		public DialogueController.Line[] m_lines;
		public Dialogue m_target;
		public bool m_loop;
		public bool m_singleUse;
		public bool m_appendToAll;
	};
	public WeightedObject<Info>[] m_dialogue;

	[Serializable] public class Expression
	{
		public string m_key;
		public string m_replacement;
	}
	public WeightedObject<Expression>[] m_expressions;
}
