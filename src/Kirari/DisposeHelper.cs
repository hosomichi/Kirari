using System;
using System.Collections.Generic;

namespace Kirari
{
    public static class DisposeHelper
    {
        public static void EnsureAllSteps(params Action[]? actions)
        {
            if (actions == null) return;
            if (actions.Length <= 0) return;

            List<Exception>? disposeExceptions = null;

            foreach (var action in actions)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    RecordException(ex);
                }
            }

            if (disposeExceptions != null && disposeExceptions.Count > 0)
            {
                switch (disposeExceptions.Count)
                {
                    case 1:
                        throw disposeExceptions[0];
                    default:
                        throw new AggregateException(disposeExceptions);
                }
            }

            void RecordException(Exception ex)
            {
                if (disposeExceptions == null)
                {
                    disposeExceptions = new List<Exception>();
                }

                disposeExceptions.Add(ex);
            }
        }
    }
}
