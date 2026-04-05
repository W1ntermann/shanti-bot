namespace ShantiBotDi.Models;

public enum UserState
{
    None,
    WaitingForLoginUsername,
    WaitingForLoginPassword,
    WaitingForRegisterUsername,
    WaitingForRegisterPassword,
    LoggedIn,
    
}