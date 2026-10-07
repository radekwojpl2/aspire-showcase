import aspireLogo from '/Aspire.png';
import './App.css';
import { Account } from './account.tsx';
import { Home, StartBusiness } from './business.tsx';
import { BookingPage } from './book.tsx';
import { MyBookingsPage } from './my-bookings.tsx';

// A few pages, so no router: the path picks the page. bff serves index.html for every path that
// isn't /api or /bff. The owner's pages (/, /bookings, /services, /staff, /hours, /time-off) are tabs of one
// dashboard, which switches between them without reloading.
function Page() {
  // The public booking page of a business: /book/{slug}.
  const booking = window.location.pathname.match(/^\/book\/([^/]+)\/?$/);
  if (booking) return <BookingPage slug={decodeURIComponent(booking[1])} />;

  switch (window.location.pathname) {
    case '/start':
      return <StartBusiness />;
    case '/my-bookings':
      return <MyBookingsPage />;
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
