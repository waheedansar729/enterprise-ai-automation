namespace Automation.Core
{
    public class TestFinding
    {
        public void Calculate()
        {
            int unusedVariable = 10; // AI yeh pakdega (Unused variable)
            string text = null;
            Console.WriteLine(text.Length); // AI yeh pakdega (Null reference risk)
        }
    }
}
