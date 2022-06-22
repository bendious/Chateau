using System;
using UnityEngine;


[CreateAssetMenu]
public class NpcDialogue : ScriptableObject
{
	[Serializable] public class DialogueInfo
	{
		public string m_preconditionName;
		public int m_userdata;
		public DialogueController.Line[] m_lines;
		public bool m_loop;
		public bool m_singleUse;
	};
	public WeightedObject<DialogueInfo>[] m_dialogue;

	[Serializable] public class ExpressionInfo
	{
		public string m_key;
		public string m_replacement;
	}
	public WeightedObject<ExpressionInfo>[] m_expressions;
}
