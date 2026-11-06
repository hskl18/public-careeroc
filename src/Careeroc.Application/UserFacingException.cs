namespace Careeroc.Application;

// An error whose message is written for the person who made the request. Endpoints answer it with
// 400 and that message; any other exception is a bug or an outage, so it becomes a 500 and its
// message stays out of the response.
public sealed class UserFacingException(string message, Exception? innerException = null)
    : Exception(message, innerException);
