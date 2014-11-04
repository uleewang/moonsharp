﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MoonSharp.Interpreter.DataStructs;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		ByteCode m_RootChunk;

		FastStack<DynValue> m_ValueStack = new FastStack<DynValue>(131072);
		FastStack<CallStackItem> m_ExecutionStack = new FastStack<CallStackItem>(131072);

		Table m_GlobalTable;
		Script m_Script;
		Processor m_Parent = null;
		CoroutineState m_State;
		bool m_CanYield = true;
		int m_SavedInstructionPtr = -1;
		DebugContext m_Debug;

		public Processor(Script script, Table globalContext, ByteCode byteCode)
		{
			m_Debug = new DebugContext();
			m_RootChunk = byteCode;
			m_GlobalTable = globalContext;
			m_Script = script;
			m_State = CoroutineState.Main;
			DynValue.NewCoroutine(new Coroutine(this)); // creates an associated coroutine for the main processor
		}

		private Processor(Processor parentProcessor)
		{
			m_Debug = parentProcessor.m_Debug;
			m_RootChunk = parentProcessor.m_RootChunk;
			m_GlobalTable = parentProcessor.m_GlobalTable;
			m_Script = parentProcessor.m_Script;
			m_Parent = parentProcessor;
			m_State = CoroutineState.NotStarted;
		}
	

		public DynValue Call(DynValue function, DynValue[] args)
		{
			EnterProcessor();

			try
			{
				var stopwatch = this.m_Script.PerformanceStats.StartStopwatch(Diagnostics.PerformanceCounter.Execution);

				m_CanYield = false;

				try
				{
					int entrypoint = PushClrToScriptStackFrame(function, args);
					return Processing_Loop(entrypoint);
				}
				finally
				{
					m_CanYield = true;

					if (stopwatch != null)
						stopwatch.Dispose();
				}
			}
			finally
			{
				LeaveProcessor();
			}
		}

		// pushes all what's required to perform a clr-to-script function call. function can be null if it's already
		// at vstack top.
		private int PushClrToScriptStackFrame(DynValue function, DynValue[] args)
		{
			if (function == null) 
				function = m_ValueStack.Peek();
			else
				m_ValueStack.Push(function);  // func val

			args = Internal_AdjustTuple(args);

			for (int i = 0; i < args.Length; i++)
				m_ValueStack.Push(args[i]);

			m_ValueStack.Push(DynValue.NewNumber(args.Length));  // func args count

			m_ExecutionStack.Push(new CallStackItem()
			{
				BasePointer = m_ValueStack.Count,
				Debug_EntryPoint = function.Function.EntryPointByteCodeLocation,
				ReturnAddress = -1,
				ClosureScope = function.Function.ClosureContext,
			});

			return function.Function.EntryPointByteCodeLocation;
		}


		int m_OwningThreadID = -1;
		int m_ExecutionNesting = 0;

		private void LeaveProcessor()
		{
			m_ExecutionNesting -= 1;

			if (m_ExecutionNesting == 0 && m_Debug != null && m_Debug.DebuggerAttached != null)
			{
				m_Debug.DebuggerAttached.SignalExecutionEnded();
			}
		}

		private void EnterProcessor()
		{
			int threadID = Thread.CurrentThread.ManagedThreadId;

			if (m_OwningThreadID >= 0 && m_OwningThreadID != threadID)
			{
				string msg = string.Format("Cannot enter the same MoonSharp processor from two different threads : {0} and {1}", m_OwningThreadID, threadID);
				throw new InvalidOperationException(msg);
			}

			m_OwningThreadID = threadID;

			m_ExecutionNesting += 1;
		}










	}
}
