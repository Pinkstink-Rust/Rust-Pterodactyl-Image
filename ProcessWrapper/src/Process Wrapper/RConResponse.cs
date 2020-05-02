namespace ProcessWrapper
{
    public struct RConResponse
    {
        public string Message { get; set; }
        public int Identifier { get; set; }
        public RConLogType Type { get; set; }
        public string Stacktrace { get; set; }
    }
}
