import aspireLogo from '/Aspire.png';
import './App.css';
import { Account } from './account.tsx';
import { Home, StartBusiness } from './business.tsx';
import { OpeningHoursPage } from './hours.tsx';

// A few pages, so no router: the path picks the page, and links reload. bff serves index.html
// for every path that isn't /api or /bff.
function Page() {
  switch (window.location.pathname) {
    case '/start':
      return <StartBusiness />;
    case '/hours':
      return <OpeningHoursPage />;
    default:
      return <Home />;
  }
}

function App() {
  return (
    <div className="app-container">
      <header className="app-header">
        <Account />
        <a href="/" className="logo-link" aria-label="Home">
          <img src={aspireLogo} className="logo" alt="Aspire logo" />
        </a>
        <h1 className="app-title">Aspire Showcase</h1>
        <p className="app-subtitle">Online booking for small businesses, orchestrated by Aspire</p>
      </header>

      <main className="main-content">
        <div className="page">
          <Page />
        </div>
      </main>

      <footer className="app-footer">
        <nav aria-label="Footer navigation">
          <a href="https://aspire.dev" target="_blank" rel="noopener noreferrer">
            Learn more about Aspire<span className="visually-hidden"> (opens in new tab)</span>
          </a>
          <a
            href="https://github.com/microsoft/aspire"
            target="_blank"
            rel="noopener noreferrer"
            className="github-link"
            aria-label="View Aspire on GitHub (opens in new tab)"
          >
            <img src="/github.svg" alt="" width="24" height="24" aria-hidden="true" />
            <span className="visually-hidden">GitHub</span>
          </a>
        </nav>
      </footer>
    </div>
  );
}

export default App;
