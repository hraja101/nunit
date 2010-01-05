// ***********************************************************************
// Copyright (c) 2007 Charlie Poole
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// ***********************************************************************

using System;
using System.Text;
using System.Threading;
using System.Collections;
using System.Reflection;
using NUnit.Framework.Api;

namespace NUnit.Framework.Internal
{
	/// <summary>
	/// Summary description for TestSuite.
	/// </summary>
	/// 
	[Serializable]
	public class TestSuite : Test
	{
		#region Fields
		/// <summary>
		/// Our collection of child tests
		/// </summary>
		private TestCollection tests = new TestCollection();

        /// <summary>
        /// The fixture setup methods for this suite
        /// </summary>
        protected MethodInfo[] fixtureSetUpMethods;

        /// <summary>
        /// The fixture teardown methods for this suite
        /// </summary>
        protected MethodInfo[] fixtureTearDownMethods;

        /// <summary>
        /// The setup methods for this suite
        /// </summary>
        protected MethodInfo[] setUpMethods;

        /// <summary>
        /// The teardown methods for this suite
        /// </summary>
        protected MethodInfo[] tearDownMethods;

        /// <summary>
        /// Set to true to suppress sorting this suite's contents
        /// </summary>
        protected bool maintainTestOrder;

        /// <summary>
        /// Arguments for use in creating a parameterized fixture
        /// </summary>
        internal object[] arguments;

        /// <summary>
        /// The System.Type of the fixture for this test suite, if there is one
        /// </summary>
        private Type fixtureType;

        /// <summary>
        /// The fixture object, if it has been created
        /// </summary>
        private object fixture;

        #endregion

		#region Constructors
		public TestSuite( string name ) 
			: base( name ) { }

		public TestSuite( string parentSuiteName, string name ) 
			: base( parentSuiteName, name ) { }

        public TestSuite(Type fixtureType)
            : this(fixtureType, null) { }

        public TestSuite(Type fixtureType, object[] arguments)
            : base(fixtureType.FullName)
        {
            string name = TypeHelper.GetDisplayName(fixtureType, arguments);
            this.Name = name;
            
            this.FullName = name;
            string nspace = fixtureType.Namespace;
            if (nspace != null && nspace != "")
                this.FullName = nspace + "." + name;
            this.fixtureType = fixtureType;
            this.arguments = arguments;
        }
        #endregion

		#region Public Methods
		public void Sort()
		{
            if (!maintainTestOrder)
            {
                this.tests.Sort();

                foreach (Test test in Tests)
                {
                    TestSuite suite = test as TestSuite;
                    if (suite != null)
                        suite.Sort();
                }
            }
		}

#if false
        public void Sort(IComparer comparer)
        {
			this.tests.Sort(comparer);

			foreach( Test test in Tests )
			{
				TestSuite suite = test as TestSuite;
				if ( suite != null )
					suite.Sort(comparer);
			}
		}
#endif

		public void Add( Test test ) 
		{
//			if( test.RunState == RunState.Runnable )
//			{
//				test.RunState = this.RunState;
//				test.IgnoreReason = this.IgnoreReason;
//			}
			test.Parent = this;
			tests.Add(test);
		}

		public void Add( object fixture )
		{
			Test test = TestFixtureBuilder.BuildFrom( fixture );
			if ( test != null )
				Add( test );
		}
		#endregion

		#region Properties
		public override IList Tests 
		{
			get { return tests; }
		}

		public override bool IsSuite
		{
			get { return true; }
		}

		public override int TestCount
		{
			get
			{
				int count = 0;

				foreach(Test test in Tests)
				{
					count += test.TestCount;
				}
				return count;
			}
		}

        public override Type FixtureType
        {
            get { return fixtureType; }
        }

        public override object Fixture
        {
            get { return fixture; }
            set { fixture = value; }
        }

        public MethodInfo[] GetSetUpMethods()
        {
            return setUpMethods;
        }

        public MethodInfo[] GetTearDownMethods()
        {
            return tearDownMethods;
        }
        #endregion

		#region Test Overrides
		public override int CountTestCases(TestFilter filter)
		{
			int count = 0;

			if(filter.Pass(this)) 
			{
				foreach(Test test in Tests)
				{
					count += test.CountTestCases(filter);
				}
			}
			return count;
		}

		public override TestResult Run(ITestListener listener, TestFilter filter)
		{
			using( new TestContext() )
			{
				TestResult suiteResult = new TestResult( this );

				listener.TestStarted( this );
				long startTime = DateTime.Now.Ticks;

				switch (this.RunState)
				{
					case RunState.Runnable:
					case RunState.Explicit:
                        if (RequiresThread || ApartmentState != GetCurrentApartment())
                            new TestSuiteThread(this).Run(suiteResult, listener, filter);
                        else
                            Run(suiteResult, listener, filter);
						break;

					default:
                    case RunState.Skipped:
				        SkipAllTests(suiteResult, listener, filter);
                        break;
                    case RunState.NotRunnable:
                        MarkAllTestsInvalid( suiteResult, listener, filter);
                        break;
                    case RunState.Ignored:
                        IgnoreAllTests(suiteResult, listener, filter);
                        break;
				}

				long stopTime = DateTime.Now.Ticks;
				double time = ((double)(stopTime - startTime)) / (double)TimeSpan.TicksPerSecond;
				suiteResult.Time = time;

				listener.TestFinished(suiteResult);
				return suiteResult;
			}
		}

        public void Run(TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            suiteResult.Success(); // Assume success
            DoOneTimeSetUp(suiteResult);

            switch( suiteResult.ResultState )
            {
                case ResultState.Failure:
                case ResultState.Error:
                    MarkTestsFailed(Tests, suiteResult, listener, filter);
                    break;
                default:
                    try
                    {
                        RunAllTests(suiteResult, listener, filter);
                    }
                    finally
                    {
                        DoOneTimeTearDown(suiteResult);
                    }
                    break;
            }
        }
		#endregion

		#region Virtual Methods
        protected virtual void DoOneTimeSetUp(TestResult suiteResult)
        {
            if (FixtureType != null)
            {
                try
                {
					// In case TestFixture was created with fixture object
					if (Fixture == null && !IsStaticClass( FixtureType ) )
						CreateUserFixture();

                    if (this.Properties["_SETCULTURE"] != null)
                        TestContext.CurrentCulture =
                            new System.Globalization.CultureInfo((string)Properties["_SETCULTURE"]);

                    if (this.Properties["_SETUICULTURE"] != null)
                        TestContext.CurrentUICulture =
                            new System.Globalization.CultureInfo((string)Properties["_SETUICULTURE"]);

                    if (this.fixtureSetUpMethods != null)
                        foreach( MethodInfo fixtureSetUp in fixtureSetUpMethods )
                            Reflect.InvokeMethod(fixtureSetUp, fixtureSetUp.IsStatic ? null : Fixture);
                }
                catch (Exception ex)
                {
                    if (ex is NUnitException || ex is System.Reflection.TargetInvocationException)
                        ex = ex.InnerException;

                    if (ex is NUnit.Framework.IgnoreException)
                    {
                        this.RunState = RunState.Ignored;
                        suiteResult.Ignore(ex.Message);
                        suiteResult.StackTrace = ex.StackTrace;
                        this.IgnoreReason = ex.Message;
                    }
                    else if (ex is NUnit.Framework.AssertionException)
                        suiteResult.Failure(ex.Message, ex.StackTrace, FailureSite.SetUp);
                    else
                        suiteResult.Error(ex, FailureSite.SetUp);
                }
            }
        }

		protected virtual void CreateUserFixture()
		{
            if (arguments != null && arguments.Length > 0)
                Fixture = Reflect.Construct(FixtureType, arguments);
            else
			    Fixture = Reflect.Construct(FixtureType);
		}

        protected virtual void DoOneTimeTearDown(TestResult suiteResult)
        {
            if ( this.Fixture != null)
            {
                try
                {
                    if (this.fixtureTearDownMethods != null)
                    {
                        int index = fixtureTearDownMethods.Length;
                        while (--index >= 0 )
                        {
                            MethodInfo fixtureTearDown = fixtureTearDownMethods[index];
                            Reflect.InvokeMethod(fixtureTearDown, fixtureTearDown.IsStatic ? null : Fixture);
                        }
                    }

					IDisposable disposable = Fixture as IDisposable;
					if (disposable != null)
						disposable.Dispose();
				}
                catch (Exception ex)
                {
					// Error in TestFixtureTearDown or Dispose causes the
					// suite to be marked as a failure, even if
					// all the contained tests passed.
					NUnitException nex = ex as NUnitException;
					if (nex != null)
						ex = nex.InnerException;


					suiteResult.Failure(ex.Message, ex.StackTrace, FailureSite.TearDown);
				}

                this.Fixture = null;
            }
        }

        #endregion

        #region Helper Methods

        private bool IsStaticClass(Type type)
        {
            return type.IsAbstract && type.IsSealed;
        }

        private void RunAllTests(
			TestResult suiteResult, ITestListener listener, TestFilter filter )
		{
            if (Properties.Contains("Timeout"))
                TestContext.TestCaseTimeout = (int)Properties["Timeout"];

            foreach (Test test in ArrayList.Synchronized(Tests))
            {
                if (filter.Pass(test))
                {
                    RunState saveRunState = test.RunState;

                    if (test.RunState == RunState.Runnable && this.RunState != RunState.Runnable && this.RunState != RunState.Explicit )
                    {
                        test.RunState = this.RunState;
                        test.IgnoreReason = this.IgnoreReason;
                    }

                    TestResult result = test.Run(listener, filter);

                    suiteResult.AddResult(result);

                    if (saveRunState != test.RunState)
                    {
                        test.RunState = saveRunState;
                        test.IgnoreReason = null;
                    }

                    if (result.ResultState == ResultState.Cancelled)
                        break;
                }
            }
		}

        private void SkipAllTests(TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            suiteResult.Skip(this.IgnoreReason);
            MarkTestsNotRun(this.Tests, ResultState.Skipped, this.IgnoreReason, suiteResult, listener, filter);
        }

        private void IgnoreAllTests(TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            suiteResult.Ignore(this.IgnoreReason);
            MarkTestsNotRun(this.Tests, ResultState.Ignored, this.IgnoreReason, suiteResult, listener, filter);
        }

        private void MarkAllTestsInvalid(TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            suiteResult.Invalid(this.IgnoreReason);
            MarkTestsNotRun(this.Tests, ResultState.NotRunnable, this.IgnoreReason, suiteResult, listener, filter);
        }
       
        private void MarkTestsNotRun(
            IList tests, ResultState resultState, string ignoreReason, TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            foreach (Test test in ArrayList.Synchronized(tests))
            {
                if (filter.Pass(test))
                    MarkTestNotRun(test, resultState, ignoreReason, suiteResult, listener, filter);
            }
        }

        private void MarkTestNotRun(
            Test test, ResultState resultState, string ignoreReason, TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            if (test is TestSuite)
            {
                listener.TestStarted(test);
                TestResult result = new TestResult( test );
				result.SetResult( resultState, ignoreReason, null );
                MarkTestsNotRun(test.Tests, resultState, ignoreReason, suiteResult, listener, filter);
                suiteResult.AddResult(result);
                listener.TestFinished(result);
            }
            else
            {
                listener.TestStarted(test);
                TestResult result = new TestResult( test );
                result.SetResult( resultState, ignoreReason, null );
                suiteResult.AddResult(result);
                listener.TestFinished(result);
            }
        }

        private void MarkTestsFailed(
            IList tests, TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            foreach (Test test in ArrayList.Synchronized(tests))
                if (filter.Pass(test))
                    MarkTestFailed(test, suiteResult, listener, filter);
        }

        private void MarkTestFailed(
            Test test, TestResult suiteResult, ITestListener listener, TestFilter filter)
        {
            if (test is TestSuite)
            {
                listener.TestStarted(test);
                TestResult result = new TestResult( test );
				string msg = string.Format( "Parent SetUp failed in {0}", this.FixtureType.Name );
				result.Failure(msg, null, FailureSite.Parent);
                MarkTestsFailed(test.Tests, suiteResult, listener, filter);
                suiteResult.AddResult(result);
                listener.TestFinished(result);
            }
            else
            {
                listener.TestStarted(test);
                TestResult result = new TestResult( test );
				string msg = string.Format( "TestFixtureSetUp failed in {0}", this.FixtureType.Name );
				result.Failure(msg, null, FailureSite.Parent);
				suiteResult.AddResult(result);
                listener.TestFinished(result);
            }
        }
        #endregion

#if CLR_2_0
        private class TestCollection : System.Collections.Generic.List<Test> { }
#else
        private class TestCollection : ArrayList { }
#endif
    }
}